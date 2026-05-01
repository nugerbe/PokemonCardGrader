using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCvSharp;
using PokemonCardGrader.Application.Configuration;
using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Infrastructure.ImageAnalysis;

/// <summary>
/// Analyzes surface, corner, and edge condition of a normalized card image.
/// Returns 1-10 scores for each category and a list of detected defects.
/// </summary>
public sealed class ConditionAnalyzer(
    IOptions<CardAnalysisOptions> options,
    ILogger<ConditionAnalyzer> logger)
{
    private readonly CardAnalysisOptions _opts = options.Value;

    public record ConditionResult(
        double SurfaceScore,
        double CornersScore,
        double EdgesScore,
        List<DetectedDefect> Defects);

    /// <summary>
    /// Runs all condition analysis on the normalized card image.
    /// </summary>
    public ConditionResult Analyze(Mat normalized)
    {
        var surface = AnalyzeSurface(normalized);
        var corners = AnalyzeCorners(normalized);
        var edges = AnalyzeEdges(normalized);
        var defects = DetectDefects(normalized);

        return new ConditionResult(surface, corners, edges, defects);
    }

    // ── Surface analysis ──

    /// <summary>
    /// Analyzes surface quality via Laplacian variance and CLAHE-enhanced grid-cell outlier detection.
    /// </summary>
    public double AnalyzeSurface(Mat normalized)
    {
        var roi = GetInnerRoi(normalized, _opts.SurfaceBorderExclude);

        try
        {
            using var gray = new Mat();
            Cv2.CvtColor(roi, gray, ColorConversionCodes.BGR2GRAY);

            using var clahe = Cv2.CreateCLAHE(_opts.ClaheClipLimit, new Size(_opts.ClaheTileSize, _opts.ClaheTileSize));
            using var enhanced = new Mat();
            clahe.Apply(gray, enhanced);

            using var laplacian = new Mat();
            Cv2.Laplacian(enhanced, laplacian, MatType.CV_64F);

            var cellH = gray.Rows / _opts.SurfaceGridRows;
            var cellW = gray.Cols / _opts.SurfaceGridCols;
            var cellVariances = new List<double>();

            for (var r = 0; r < _opts.SurfaceGridRows; r++)
            {
                for (var c = 0; c < _opts.SurfaceGridCols; c++)
                {
                    var cellRect = new Rect(c * cellW, r * cellH, cellW, cellH);
                    if (cellRect.X + cellRect.Width > laplacian.Cols ||
                        cellRect.Y + cellRect.Height > laplacian.Rows)
                        continue;

                    using var cell = new Mat(laplacian, cellRect);
                    Cv2.MeanStdDev(cell, out _, out var stddev);
                    cellVariances.Add(stddev.Val0);
                }
            }

            if (cellVariances.Count == 0)
                return 8.0;

            var median = cellVariances.OrderBy(v => v).ElementAt(cellVariances.Count / 2);
            var outliers = cellVariances.Count(v => v > median * _opts.SurfaceOutlierMultiplier);
            var outlierRatio = (double)outliers / cellVariances.Count;

            Cv2.MeanStdDev(laplacian, out _, out var overallStd);
            var sharpness = overallStd.Val0;

            var score = 10.0;
            score -= outlierRatio * 15.0;

            if (sharpness < _opts.MinSharpness)
                score -= 1.0;

            return Math.Clamp(Math.Round(score * 2) / 2, 1.0, 10.0);
        }
        finally
        {
            roi.Dispose();
        }
    }

    // ── Corner analysis ──

    /// <summary>
    /// Analyzes corner quality by examining whitening and sharpness in the four corner regions.
    /// </summary>
    public double AnalyzeCorners(Mat normalized)
    {
        var w = _opts.NormalizedWidth;
        var h = _opts.NormalizedHeight;
        var cornerSize = (int)(w * _opts.CornerSizeFraction);

        var cornerRects = new[]
        {
            new Rect(0, 0, cornerSize, cornerSize),
            new Rect(w - cornerSize, 0, cornerSize, cornerSize),
            new Rect(0, h - cornerSize, cornerSize, cornerSize),
            new Rect(w - cornerSize, h - cornerSize, cornerSize, cornerSize)
        };

        var cornerScores = cornerRects.Select(rect => AnalyzeCornerRegion(normalized, rect)).ToList();

        var worstCorner = cornerScores.Min();
        var avgCorner = cornerScores.Average();

        var finalScore = (worstCorner * _opts.CornerWorstWeight) + (avgCorner * (1 - _opts.CornerWorstWeight));
        return Math.Clamp(Math.Round(finalScore * 2) / 2, 1.0, 10.0);
    }

    private double AnalyzeCornerRegion(Mat normalized, Rect region)
    {
        using var corner = new Mat(normalized, region);
        using var gray = new Mat();
        Cv2.CvtColor(corner, gray, ColorConversionCodes.BGR2GRAY);

        using var thresh = new Mat();
        Cv2.Threshold(gray, thresh, _opts.CornerWhiteThreshold, 255, ThresholdTypes.Binary);
        var whitePixelRatio = (double)Cv2.CountNonZero(thresh) / (region.Width * region.Height);

        using var laplacian = new Mat();
        Cv2.Laplacian(gray, laplacian, MatType.CV_64F);
        Cv2.MeanStdDev(laplacian, out _, out var stddev);
        var sharpness = stddev.Val0;

        var score = 10.0;

        if (whitePixelRatio > _opts.CornerWhiteRatioThreshold)
            score -= (whitePixelRatio - _opts.CornerWhiteRatioThreshold) * 10.0;

        if (sharpness < _opts.CornerMinSharpness)
            score -= (_opts.CornerMinSharpness - sharpness) * 0.15;

        return Math.Clamp(score, 1.0, 10.0);
    }

    // ── Edge analysis ──

    /// <summary>
    /// Analyzes edge quality by examining strips along each card edge for whitening and damage.
    /// </summary>
    public double AnalyzeEdges(Mat normalized)
    {
        var w = _opts.NormalizedWidth;
        var h = _opts.NormalizedHeight;
        var inset = (int)(w * _opts.EdgeInsetFraction);
        var edgeWidth = (int)(w * _opts.EdgeWidthFraction);
        var insetV = (int)(h * _opts.EdgeInsetFraction);
        var edgeHeightV = (int)(h * _opts.EdgeWidthFraction);

        var edgeRects = new[]
        {
            new Rect(inset, 0, edgeWidth, h),
            new Rect(w - inset - edgeWidth, 0, edgeWidth, h),
            new Rect(0, insetV, w, edgeHeightV),
            new Rect(0, h - insetV - edgeHeightV, w, edgeHeightV)
        };

        var edgeScores = edgeRects.Select(rect => AnalyzeEdgeStrip(normalized, rect)).ToList();

        var worstEdge = edgeScores.Min();
        var avgEdge = edgeScores.Average();

        var finalScore = (worstEdge * _opts.EdgeWorstWeight) + (avgEdge * (1 - _opts.EdgeWorstWeight));
        return Math.Clamp(Math.Round(finalScore * 2) / 2, 1.0, 10.0);
    }

    private double AnalyzeEdgeStrip(Mat normalized, Rect region)
    {
        using var edge = new Mat(normalized, region);
        using var gray = new Mat();
        Cv2.CvtColor(edge, gray, ColorConversionCodes.BGR2GRAY);

        Cv2.MeanStdDev(gray, out var meanVal, out var stdVal);
        var medianEstimate = meanVal.Val0;

        var whiteThresh = medianEstimate >= 180 ? _opts.EdgeWhiteThresholdLight : _opts.EdgeWhiteThresholdDark;

        using var thresh = new Mat();
        Cv2.Threshold(gray, thresh, whiteThresh, 255, ThresholdTypes.Binary);
        var whiteRatio = (double)Cv2.CountNonZero(thresh) / (region.Width * region.Height);

        using var sobelX = new Mat();
        using var sobelY = new Mat();
        Cv2.Sobel(gray, sobelX, MatType.CV_64F, 1, 0);
        Cv2.Sobel(gray, sobelY, MatType.CV_64F, 0, 1);

        Cv2.MeanStdDev(sobelX, out _, out var stdX);
        Cv2.MeanStdDev(sobelY, out _, out var stdY);

        var gradientMagnitude = Math.Sqrt(stdX.Val0 * stdX.Val0 + stdY.Val0 * stdY.Val0);

        var uniformity = stdVal.Val0;
        var gradientPenaltyWeight = uniformity > 40 ? 0.025 : 0.01;

        var score = 10.0;

        if (whiteRatio > _opts.EdgeWhiteRatioThreshold)
            score -= (whiteRatio - _opts.EdgeWhiteRatioThreshold) * 5.0;

        if (gradientMagnitude > _opts.EdgeGradientThreshold)
            score -= (gradientMagnitude - _opts.EdgeGradientThreshold) * gradientPenaltyWeight;

        return Math.Clamp(score, 1.0, 10.0);
    }

    // ── Defect detection ──

    /// <summary>
    /// Detects visible defects (scratches, dents) using morphological gradient and cross-threshold validation.
    /// </summary>
    public List<DetectedDefect> DetectDefects(Mat normalized)
    {
        var w = _opts.NormalizedWidth;
        var h = _opts.NormalizedHeight;
        var defects = new List<DetectedDefect>();

        using var gray = new Mat();
        Cv2.CvtColor(normalized, gray, ColorConversionCodes.BGR2GRAY);

        using var morphKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3));
        using var gradient = new Mat();
        Cv2.MorphologyEx(gray, gradient, MorphTypes.Gradient, morphKernel);

        var threshMaps = new List<Mat>();
        try
        {
            foreach (var t in _opts.DefectThresholds)
            {
                var m = new Mat();
                Cv2.Threshold(gradient, m, t, 255, ThresholdTypes.Binary);
                threshMaps.Add(m);
            }

            // Primary detection at middle threshold
            var primaryIdx = threshMaps.Count / 2;
            Cv2.FindContours(threshMaps[primaryIdx], out var contours, out _,
                RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            var maxDefectArea = w * h * _opts.DefectMaxAreaFraction;

            foreach (var contour in contours)
            {
                var area = Cv2.ContourArea(contour);
                if (area < _opts.DefectMinArea || area > maxDefectArea)
                    continue;

                var boundingRect = Cv2.BoundingRect(contour);
                var aspectRatio = (double)boundingRect.Width / Math.Max(boundingRect.Height, 1);

                string defectType;
                double severity;

                if (aspectRatio > _opts.ScratchAspectThreshold || aspectRatio < 1.0 / _opts.ScratchAspectThreshold)
                {
                    defectType = "scratch";
                    severity = Math.Min(area / 200.0, 1.0);
                }
                else if (area > _opts.DentMinArea)
                {
                    defectType = "dent";
                    severity = Math.Min(area / 1000.0, 1.0);
                }
                else
                {
                    continue;
                }

                var confidence = ComputeDefectConfidence(contour, boundingRect, area, gradient, threshMaps);

                defects.Add(new DetectedDefect
                {
                    Type = defectType,
                    Severity = Math.Round(severity, 2),
                    X = Math.Round((double)boundingRect.X / w, 3),
                    Y = Math.Round((double)boundingRect.Y / h, 3),
                    Width = Math.Round((double)boundingRect.Width / w, 3),
                    Height = Math.Round((double)boundingRect.Height / h, 3),
                    Confidence = Math.Round(confidence, 2)
                });
            }
        }
        finally
        {
            foreach (var m in threshMaps) m.Dispose();
        }

        return defects
            .OrderByDescending(d => d.Severity)
            .Take(_opts.MaxDefects)
            .ToList();
    }

    private double ComputeDefectConfidence(
        Point[] contour, Rect boundingRect, double area,
        Mat gradient, List<Mat> threshMaps)
    {
        // Factor 1: Solidity
        var hull = Cv2.ConvexHull(contour);
        var hullArea = Cv2.ContourArea(hull);
        var solidityScore = hullArea > 0 ? Math.Clamp(area / hullArea, 0, 1) : 0;

        // Factor 2: Gradient strength
        var safeRect = new Rect(
            Math.Max(0, boundingRect.X),
            Math.Max(0, boundingRect.Y),
            Math.Min(boundingRect.Width, gradient.Cols - Math.Max(0, boundingRect.X)),
            Math.Min(boundingRect.Height, gradient.Rows - Math.Max(0, boundingRect.Y)));

        double gradientScore = 0.5;
        if (safeRect.Width > 0 && safeRect.Height > 0)
        {
            using var roi = new Mat(gradient, safeRect);
            var meanGrad = Cv2.Mean(roi).Val0;
            gradientScore = Math.Clamp(meanGrad / 100.0, 0, 1);
        }

        // Factor 3: Cross-threshold validation
        var crossCount = 0;
        var centerX = boundingRect.X + boundingRect.Width / 2;
        var centerY = boundingRect.Y + boundingRect.Height / 2;
        var checkRadius = Math.Max(3, (int)Math.Sqrt(area) / 2);

        foreach (var threshMap in threshMaps)
        {
            var found = false;
            for (var dy = -checkRadius; dy <= checkRadius && !found; dy += Math.Max(1, checkRadius / 2))
            {
                for (var dx = -checkRadius; dx <= checkRadius && !found; dx += Math.Max(1, checkRadius / 2))
                {
                    var px = centerX + dx;
                    var py = centerY + dy;
                    if (px >= 0 && px < threshMap.Cols && py >= 0 && py < threshMap.Rows
                        && threshMap.At<byte>(py, px) > 0)
                    {
                        found = true;
                    }
                }
            }
            if (found) crossCount++;
        }

        var crossScore = (double)crossCount / threshMaps.Count;

        var confidence = (solidityScore * 0.25) + (gradientScore * 0.35) + (crossScore * 0.40);
        return Math.Clamp(confidence, _opts.DefectMinConfidence, _opts.DefectMaxConfidence);
    }

    // ── Helpers ──

    private static Mat GetInnerRoi(Mat normalized, double borderFraction)
    {
        var x = (int)(normalized.Cols * borderFraction);
        var y = (int)(normalized.Rows * borderFraction);
        var w = normalized.Cols - (2 * x);
        var h = normalized.Rows - (2 * y);
        return new Mat(normalized, new Rect(x, y, w, h));
    }
}
