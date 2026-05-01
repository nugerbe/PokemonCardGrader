using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCvSharp;
using PokemonCardGrader.Application.Configuration;
using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Infrastructure.ImageAnalysis;

/// <summary>
/// Evaluates pipeline confidence by analyzing image quality, detection reliability,
/// and CV/ML agreement. Returns a <see cref="ConfidenceBreakdown"/> with component
/// scores and an overall confidence value. Overall below 0.5 flags "low confidence".
/// </summary>
public sealed class ConfidenceScorer(
    IOptions<CardAnalysisOptions> options,
    ILogger<ConfidenceScorer> logger)
{
    private readonly CardAnalysisOptions _opts = options.Value;

    /// <summary>
    /// Input data for confidence scoring, collected from the analysis pipeline.
    /// </summary>
    public sealed record ConfidenceInput(
        Mat NormalizedImage,
        bool CardDetected,
        bool UsedFullFrameFallback,
        BorderLines? DetectedBorders,
        List<DetectedDefect> CvDefects,
        List<DetectedDefect> MlDefects,
        double? CvSurfaceScore,
        double? MlSurfaceScore,
        bool DefectModelAvailable,
        bool SurfaceModelAvailable);

    /// <summary>
    /// Computes a full confidence breakdown from pipeline data.
    /// </summary>
    public ConfidenceBreakdown Score(ConfidenceInput input)
    {
        var imageQuality = EvaluateImageQuality(input.NormalizedImage);
        var detectionReliability = EvaluateDetectionReliability(
            input.CardDetected, input.UsedFullFrameFallback, input.DetectedBorders);
        var cvMlAgreement = EvaluateCvMlAgreement(
            input.CvDefects, input.MlDefects,
            input.CvSurfaceScore, input.MlSurfaceScore,
            input.DefectModelAvailable, input.SurfaceModelAvailable);
        var borderConsistency = EvaluateBorderConsistency(input.DetectedBorders);

        var overall =
            imageQuality * _opts.ConfidenceWeightImageQuality +
            detectionReliability * _opts.ConfidenceWeightDetection +
            cvMlAgreement * _opts.ConfidenceWeightCvMlAgreement +
            borderConsistency * _opts.ConfidenceWeightBorderConsistency;

        overall = Math.Clamp(overall, 0.0, 1.0);

        var summary = BuildSummary(imageQuality, detectionReliability, cvMlAgreement,
            borderConsistency, overall, input.DefectModelAvailable);

        logger.LogDebug(
            "Confidence: overall={Overall:F3} (imgQ={ImgQ:F3}, detect={Detect:F3}, cvml={CvMl:F3}, border={Border:F3})",
            overall, imageQuality, detectionReliability, cvMlAgreement, borderConsistency);

        return new ConfidenceBreakdown
        {
            ImageQuality = Math.Round(imageQuality, 3),
            DetectionReliability = Math.Round(detectionReliability, 3),
            CvMlAgreement = Math.Round(cvMlAgreement, 3),
            BorderConsistency = Math.Round(borderConsistency, 3),
            Overall = Math.Round(overall, 3),
            Summary = summary
        };
    }

    // ── Component Evaluators ──

    /// <summary>
    /// Evaluates input image quality based on sharpness, exposure, and noise.
    /// </summary>
    private double EvaluateImageQuality(Mat normalizedImage)
    {
        using var gray = new Mat();
        Cv2.CvtColor(normalizedImage, gray, ColorConversionCodes.BGR2GRAY);

        // Factor 1: Sharpness via Laplacian variance
        using var laplacian = new Mat();
        Cv2.Laplacian(gray, laplacian, MatType.CV_64F);
        Cv2.MeanStdDev(laplacian, out _, out var lapStd);
        var sharpness = lapStd.Val0;
        var sharpnessScore = Math.Clamp(sharpness / (_opts.MinSharpnessForQuality * 3), 0, 1);

        // Factor 2: Exposure — mean brightness should be in ideal range
        Cv2.MeanStdDev(gray, out var meanBrightness, out var stdBrightness);
        var brightness = meanBrightness.Val0;
        double exposureScore;
        if (brightness < _opts.IdealBrightnessMin)
            exposureScore = Math.Clamp(brightness / _opts.IdealBrightnessMin, 0, 1);
        else if (brightness > _opts.IdealBrightnessMax)
            exposureScore = Math.Clamp((255 - brightness) / (255 - _opts.IdealBrightnessMax), 0, 1);
        else
            exposureScore = 1.0;

        // Factor 3: Noise estimate via high-frequency content ratio
        // Compare original stddev with blurred version — large difference = noise
        using var blurred = new Mat();
        Cv2.GaussianBlur(gray, blurred, new Size(5, 5), 0);
        Cv2.MeanStdDev(blurred, out _, out var blurStd);
        var noiseRatio = stdBrightness.Val0 > 0
            ? Math.Abs(stdBrightness.Val0 - blurStd.Val0) / stdBrightness.Val0
            : 0;
        // Low noise ratio = good (image content, not noise)
        // High noise ratio = bad (lots of high-freq noise removed by blur)
        var noiseScore = Math.Clamp(1.0 - noiseRatio * 3, 0, 1);

        // Weighted combination
        return sharpnessScore * 0.50 + exposureScore * 0.30 + noiseScore * 0.20;
    }

    /// <summary>
    /// Evaluates how reliably the card was detected.
    /// </summary>
    private static double EvaluateDetectionReliability(
        bool cardDetected, bool usedFullFrameFallback, BorderLines? borders)
    {
        if (!cardDetected)
            return 0.1; // Near-zero but not absolute zero

        var score = 1.0;

        // Full-frame fallback = lower confidence in boundary accuracy
        if (usedFullFrameFallback)
            score -= 0.35;

        // No border detection = can't measure centering reliably
        if (borders is null)
            score -= 0.25;

        return Math.Clamp(score, 0, 1);
    }

    /// <summary>
    /// Evaluates agreement between CV and ML defect detections.
    /// When no ML model is available, returns a neutral score (0.6).
    /// </summary>
    private double EvaluateCvMlAgreement(
        List<DetectedDefect> cvDefects, List<DetectedDefect> mlDefects,
        double? cvSurface, double? mlSurface,
        bool defectModelAvailable, bool surfaceModelAvailable)
    {
        // If no ML models available, return neutral (no disagreement, no confirmation)
        if (!defectModelAvailable && !surfaceModelAvailable)
            return 0.6;

        var scores = new List<double>();

        // Defect agreement: measure spatial overlap between CV and ML detections
        if (defectModelAvailable)
        {
            var defectAgreement = ComputeDefectAgreement(cvDefects, mlDefects);
            scores.Add(defectAgreement);
        }

        // Surface score agreement: how close are CV and ML surface scores
        if (surfaceModelAvailable && cvSurface.HasValue && mlSurface.HasValue)
        {
            var surfaceDiff = Math.Abs(cvSurface.Value - mlSurface.Value);
            // Perfect agreement at 0 diff, drops to 0 at 5-point difference
            var surfaceAgreement = Math.Clamp(1.0 - surfaceDiff / 5.0, 0, 1);
            scores.Add(surfaceAgreement);
        }

        return scores.Count > 0 ? scores.Average() : 0.6;
    }

    /// <summary>
    /// Computes spatial agreement between CV and ML defect lists using IoU-based matching.
    /// Returns 1.0 for perfect agreement, decreasing with unmatched detections.
    /// </summary>
    private double ComputeDefectAgreement(List<DetectedDefect> cvDefects, List<DetectedDefect> mlDefects)
    {
        // Both empty = full agreement (clean card)
        if (cvDefects.Count == 0 && mlDefects.Count == 0)
            return 1.0;

        // One empty, other has detections = partial disagreement
        if (cvDefects.Count == 0 || mlDefects.Count == 0)
        {
            var unmatchedCount = Math.Max(cvDefects.Count, mlDefects.Count);
            return Math.Clamp(1.0 - unmatchedCount * 0.15, 0.2, 0.8);
        }

        // Match detections by spatial overlap (IoU > 0.2 and same type = match)
        var matched = 0;
        var mlUsed = new HashSet<int>();

        foreach (var cvDef in cvDefects)
        {
            for (var j = 0; j < mlDefects.Count; j++)
            {
                if (mlUsed.Contains(j)) continue;

                var mlDef = mlDefects[j];
                var iou = ComputeIoU(cvDef, mlDef);

                if (iou > 0.2 && cvDef.Type == mlDef.Type)
                {
                    matched++;
                    mlUsed.Add(j);
                    break;
                }
            }
        }

        var totalDetections = cvDefects.Count + mlDefects.Count;
        var matchRatio = (2.0 * matched) / totalDetections; // F1-style score
        return Math.Clamp(matchRatio, 0, 1);
    }

    /// <summary>
    /// Computes Intersection over Union between two defect bounding boxes.
    /// </summary>
    private static double ComputeIoU(DetectedDefect a, DetectedDefect b)
    {
        var ax1 = a.X;
        var ay1 = a.Y;
        var ax2 = a.X + a.Width;
        var ay2 = a.Y + a.Height;

        var bx1 = b.X;
        var by1 = b.Y;
        var bx2 = b.X + b.Width;
        var by2 = b.Y + b.Height;

        var ix1 = Math.Max(ax1, bx1);
        var iy1 = Math.Max(ay1, by1);
        var ix2 = Math.Min(ax2, bx2);
        var iy2 = Math.Min(ay2, by2);

        var interWidth = Math.Max(0, ix2 - ix1);
        var interHeight = Math.Max(0, iy2 - iy1);
        var interArea = interWidth * interHeight;

        var aArea = a.Width * a.Height;
        var bArea = b.Width * b.Height;
        var unionArea = aArea + bArea - interArea;

        return unionArea > 0 ? interArea / unionArea : 0;
    }

    /// <summary>
    /// Evaluates border detection consistency from the detected border positions.
    /// Checks whether borders are within reasonable card geometry ranges.
    /// </summary>
    private double EvaluateBorderConsistency(BorderLines? borders)
    {
        if (borders is null)
            return 0.3;

        var score = 1.0;

        // Borders should be within plausible range
        var left = borders.LeftBorderX;
        var right = borders.RightBorderX;
        var top = borders.TopBorderY;
        var bottom = borders.BottomBorderY;

        // Check that borders form a valid region
        if (right <= left || bottom <= top)
            return 0.1;

        // Penalize if borders are too close to edges (unrealistic)
        if (left < 0.01 || top < 0.01 || right > 0.99 || bottom > 0.99)
            score -= 0.2;

        // Penalize extreme asymmetry (left vs right border width)
        var leftWidth = left;
        var rightWidth = 1.0 - right;
        var topHeight = top;
        var bottomHeight = 1.0 - bottom;

        var lrRatio = Math.Min(leftWidth, rightWidth) / Math.Max(leftWidth, rightWidth + 0.001);
        var tbRatio = Math.Min(topHeight, bottomHeight) / Math.Max(topHeight, bottomHeight + 0.001);

        // Very extreme asymmetry (>4:1) is suspicious — likely detection error
        if (lrRatio < 0.25)
            score -= 0.15;
        if (tbRatio < 0.25)
            score -= 0.15;

        // Border width should be between 2% and 15% of card dimension
        var avgBorderWidth = (leftWidth + rightWidth + topHeight + bottomHeight) / 4;
        if (avgBorderWidth < 0.02 || avgBorderWidth > 0.15)
            score -= 0.1;

        return Math.Clamp(score, 0, 1);
    }

    // ── Summary Builder ──

    private string BuildSummary(
        double imageQuality, double detection, double cvMl,
        double borderConsistency, double overall, bool mlAvailable)
    {
        var parts = new List<string>();

        if (overall >= 0.8)
            parts.Add("High confidence");
        else if (overall >= 0.5)
            parts.Add("Moderate confidence");
        else
            parts.Add("Low confidence — results may be unreliable");

        if (imageQuality < 0.4)
            parts.Add("image quality is poor (blurry or poorly exposed)");
        else if (imageQuality < 0.6)
            parts.Add("image quality is acceptable but not ideal");

        if (detection < 0.5)
            parts.Add("card boundary detection was uncertain");

        if (mlAvailable && cvMl < 0.4)
            parts.Add("CV and ML analyses disagree significantly");
        else if (!mlAvailable)
            parts.Add("no ML model available for cross-validation");

        if (borderConsistency < 0.5)
            parts.Add("border detection was inconsistent");

        return string.Join(" — ", parts) + ".";
    }
}
