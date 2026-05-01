using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCvSharp;
using PokemonCardGrader.Application.Configuration;
using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Infrastructure.ImageAnalysis;

/// <summary>
/// Phase 13: Advanced CV defect analysis with region-aware inspection.
/// Performs edge continuity scoring (chipping), corner curvature analysis (rounding),
/// texture analysis (scratches/print lines), and color deviation (whitening).
/// </summary>
public sealed class AdvancedDefectAnalyzer(
    IOptions<CardAnalysisOptions> options,
    ILogger<AdvancedDefectAnalyzer> logger)
{
    private readonly CardAnalysisOptions _opts = options.Value;

    public sealed record AdvancedDefectResult(
        double EdgeContinuityScore,
        double[] EdgeContinuityPerSide,
        double CornerCurvatureScore,
        double[] CornerCurvaturePerCorner,
        double TextureScore,
        double ColorDeviationScore,
        List<DetectedDefect> AdvancedDefects);

    /// <summary>
    /// Performs advanced defect analysis on the normalized card image using region segmentation.
    /// </summary>
    public AdvancedDefectResult Analyze(Mat normalized, CardRegions regions)
    {
        var advancedDefects = new List<DetectedDefect>();

        var (edgeContinuity, edgeContinuityPerSide) = AnalyzeEdgeContinuity(normalized, regions, advancedDefects);
        var (cornerCurvature, cornerCurvaturePerCorner) = AnalyzeCornerCurvature(normalized, regions, advancedDefects);
        var textureScore = AnalyzeTexture(normalized, regions, advancedDefects);
        var colorDeviation = AnalyzeColorDeviation(normalized, regions, advancedDefects);

        logger.LogInformation(
            "AdvancedDefects: edges={EdgeScore:F2} corners={CornerScore:F2} " +
            "texture={TextureScore:F2} color={ColorScore:F2} defects={Count}",
            edgeContinuity, cornerCurvature, textureScore, colorDeviation, advancedDefects.Count);

        return new AdvancedDefectResult(
            edgeContinuity, edgeContinuityPerSide,
            cornerCurvature, cornerCurvaturePerCorner,
            textureScore, colorDeviation,
            advancedDefects);
    }

    private (double Overall, double[] PerSide) AnalyzeEdgeContinuity(
        Mat normalized, CardRegions regions, List<DetectedDefect> defects)
    {
        using var gray = new Mat();
        Cv2.CvtColor(normalized, gray, ColorConversionCodes.BGR2GRAY);

        var perSide = new double[4]; // Top, Right, Bottom, Left
        var sideLabels = new[] { "TopEdge", "RightEdge", "BottomEdge", "LeftEdge" };

        for (var i = 0; i < 4; i++)
        {
            var edgeZone = regions.EdgeZones[i];
            using var region = RegionSegmenter.ExtractRegion(gray, edgeZone);

            // Compute gradient along the edge
            using var sobelX = new Mat();
            using var sobelY = new Mat();
            Cv2.Sobel(region, sobelX, MatType.CV_64F, 1, 0, ksize: 3);
            Cv2.Sobel(region, sobelY, MatType.CV_64F, 0, 1, ksize: 3);

            using var magnitude = new Mat();
            Cv2.Magnitude(sobelX, sobelY, magnitude);

            // Sample points along the edge for continuity
            var samples = _opts.EdgeContinuitySamples;
            var isHorizontal = i == 0 || i == 2; // Top/Bottom edges
            var stepLength = isHorizontal ? region.Cols : region.Rows;
            var step = Math.Max(1, stepLength / samples);

            var gradients = new List<double>();
            for (var s = 0; s < stepLength; s += step)
            {
                double maxGrad = 0;
                if (isHorizontal)
                {
                    for (var row = 0; row < region.Rows; row++)
                    {
                        if (s < magnitude.Cols)
                            maxGrad = Math.Max(maxGrad, magnitude.At<double>(row, s));
                    }
                }
                else
                {
                    for (var col = 0; col < region.Cols; col++)
                    {
                        if (s < magnitude.Rows)
                            maxGrad = Math.Max(maxGrad, magnitude.At<double>(s, col));
                    }
                }
                gradients.Add(maxGrad);
            }

            if (gradients.Count == 0)
            {
                perSide[i] = 1.0;
                continue;
            }

            // Continuity = 1 - (fraction of high-gradient breaks)
            var breakCount = gradients.Count(g => g > _opts.ChipGradientThreshold);
            var continuity = 1.0 - (double)breakCount / gradients.Count;
            perSide[i] = Math.Clamp(continuity, 0, 1);

            // Report chipping defects
            if (continuity < 0.85)
            {
                var severity = 1.0 - continuity;
                defects.Add(new DetectedDefect
                {
                    Type = "edge_chipping",
                    Severity = Math.Round(severity, 3),
                    X = (double)edgeZone.X / normalized.Width,
                    Y = (double)edgeZone.Y / normalized.Height,
                    Width = (double)edgeZone.Width / normalized.Width,
                    Height = (double)edgeZone.Height / normalized.Height,
                    Confidence = Math.Clamp(0.5 + severity * 0.4, 0, 0.95)
                });
            }
        }

        var overall = perSide.Average();
        return (overall, perSide);
    }

    private (double Overall, double[] PerCorner) AnalyzeCornerCurvature(
        Mat normalized, CardRegions regions, List<DetectedDefect> defects)
    {
        using var gray = new Mat();
        Cv2.CvtColor(normalized, gray, ColorConversionCodes.BGR2GRAY);

        var perCorner = new double[4];
        var cornerLabels = new[] { "TopLeft", "TopRight", "BottomRight", "BottomLeft" };

        for (var i = 0; i < 4; i++)
        {
            var cornerZone = regions.CornerZones[i];
            using var region = RegionSegmenter.ExtractRegion(gray, cornerZone);

            // Edge detection in the corner region
            using var edges = new Mat();
            Cv2.Canny(region, edges, 50, 150);

            // Find contours in the corner region
            Cv2.FindContours(edges, out var contours, out _, RetrievalModes.External,
                ContourApproximationModes.ApproxNone);

            if (contours.Length == 0)
            {
                perCorner[i] = 0.5; // Uncertain
                continue;
            }

            // Fit a circle to the corner contour to estimate curvature radius
            var allPoints = contours.SelectMany(c => c).ToArray();
            if (allPoints.Length < 5)
            {
                perCorner[i] = 0.5;
                continue;
            }

            // Use minimum enclosing circle as a proxy for corner roundness
            Cv2.MinEnclosingCircle(allPoints, out _, out var radius);

            // Compare to expected corner radius
            var deviation = Math.Abs(radius - _opts.ExpectedCornerRadius);
            var score = deviation <= _opts.CornerRadiusTolerance
                ? 1.0
                : Math.Max(0.2, 1.0 - (deviation - _opts.CornerRadiusTolerance) / (2 * _opts.CornerRadiusTolerance));

            perCorner[i] = Math.Clamp(score, 0, 1);

            // Report corner rounding defects
            if (score < 0.7)
            {
                defects.Add(new DetectedDefect
                {
                    Type = "corner_damage",
                    Severity = Math.Round(1.0 - score, 3),
                    X = (double)cornerZone.X / normalized.Width,
                    Y = (double)cornerZone.Y / normalized.Height,
                    Width = (double)cornerZone.Width / normalized.Width,
                    Height = (double)cornerZone.Height / normalized.Height,
                    Confidence = Math.Clamp(0.4 + (1.0 - score) * 0.5, 0, 0.95)
                });
            }
        }

        var overall = perCorner.Average();
        return (overall, perCorner);
    }

    private double AnalyzeTexture(Mat normalized, CardRegions regions, List<DetectedDefect> defects)
    {
        using var gray = new Mat();
        Cv2.CvtColor(normalized, gray, ColorConversionCodes.BGR2GRAY);

        // Analyze surface texture in the inner region using Gabor-like filters
        using var innerRegion = RegionSegmenter.ExtractRegion(gray, regions.InnerRegion);

        // Use multiple oriented Laplacian/Sobel responses as texture features
        var orientations = _opts.TextureGaborOrientations;
        var scratchResponses = new List<double>();

        for (var o = 0; o < orientations; o++)
        {
            var angle = o * 180.0 / orientations;

            // Create oriented kernel
            var ksize = 11;
            using var kernel = CreateOrientedKernel(ksize, angle, _opts.TextureGaborFrequency);

            using var response = new Mat();
            Cv2.Filter2D(innerRegion, response, MatType.CV_64F, kernel);

            Cv2.MeanStdDev(response, out _, out var stddev);
            scratchResponses.Add(stddev.Val0);
        }

        // High directional response indicates scratches/print lines
        var maxResponse = scratchResponses.Max();
        var meanResponse = scratchResponses.Average();
        var anisotropy = meanResponse > 0 ? maxResponse / meanResponse : 1.0;

        // Anisotropy > 2 suggests directional artifacts (scratches)
        double textureScore;
        if (anisotropy < 1.5)
        {
            textureScore = 1.0;
        }
        else if (anisotropy < 3.0)
        {
            textureScore = 1.0 - (anisotropy - 1.5) / 3.0;
            if (anisotropy > 2.0)
            {
                defects.Add(new DetectedDefect
                {
                    Type = "scratch_pattern",
                    Severity = Math.Round((anisotropy - 1.5) / 3.0, 3),
                    X = (double)regions.InnerRegion.X / normalized.Width,
                    Y = (double)regions.InnerRegion.Y / normalized.Height,
                    Width = (double)regions.InnerRegion.Width / normalized.Width,
                    Height = (double)regions.InnerRegion.Height / normalized.Height,
                    Confidence = Math.Clamp(0.3 + anisotropy * 0.1, 0, 0.85)
                });
            }
        }
        else
        {
            textureScore = 0.3;
        }

        return Math.Clamp(textureScore, 0, 1);
    }

    private double AnalyzeColorDeviation(Mat normalized, CardRegions regions, List<DetectedDefect> defects)
    {
        // Convert to Lab color space for perceptual color comparison
        using var lab = new Mat();
        Cv2.CvtColor(normalized, lab, ColorConversionCodes.BGR2Lab);

        // Analyze edges for whitening (color deviation from card body)
        var edgeDeviations = new List<double>();

        for (var i = 0; i < 4; i++)
        {
            var edgeZone = regions.EdgeZones[i];
            using var edgeRegion = RegionSegmenter.ExtractRegion(lab, edgeZone);
            var edgeMean = Cv2.Mean(edgeRegion);

            // Compare to the inner region color
            using var innerRegion = RegionSegmenter.ExtractRegion(lab, regions.InnerRegion);
            var innerMean = Cv2.Mean(innerRegion);

            // Delta E approximation (CIE76)
            var dL = edgeMean.Val0 - innerMean.Val0;
            var dA = edgeMean.Val1 - innerMean.Val1;
            var dB = edgeMean.Val2 - innerMean.Val2;
            var deltaE = Math.Sqrt(dL * dL + dA * dA + dB * dB);

            edgeDeviations.Add(deltaE);

            if (deltaE > _opts.ColorDeviationThreshold)
            {
                var severity = Math.Min(1.0, (deltaE - _opts.ColorDeviationThreshold) / 30.0);
                defects.Add(new DetectedDefect
                {
                    Type = "edge_whitening",
                    Severity = Math.Round(severity, 3),
                    X = (double)edgeZone.X / normalized.Width,
                    Y = (double)edgeZone.Y / normalized.Height,
                    Width = (double)edgeZone.Width / normalized.Width,
                    Height = (double)edgeZone.Height / normalized.Height,
                    Confidence = Math.Clamp(0.5 + severity * 0.3, 0, 0.9)
                });
            }
        }

        var maxDeviation = edgeDeviations.Count > 0 ? edgeDeviations.Max() : 0;
        var colorScore = maxDeviation <= _opts.ColorDeviationThreshold
            ? 1.0
            : Math.Max(0.2, 1.0 - (maxDeviation - _opts.ColorDeviationThreshold) / 40.0);

        return Math.Clamp(colorScore, 0, 1);
    }

    private static Mat CreateOrientedKernel(int size, double angleDeg, double frequency)
    {
        var kernel = new Mat(size, size, MatType.CV_64F);
        var center = size / 2;
        var sigma = size / 4.0;
        var theta = angleDeg * Math.PI / 180.0;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var dx = x - center;
                var dy = y - center;
                var xTheta = dx * Math.Cos(theta) + dy * Math.Sin(theta);
                var yTheta = -dx * Math.Sin(theta) + dy * Math.Cos(theta);
                var gaussian = Math.Exp(-(xTheta * xTheta + yTheta * yTheta) / (2 * sigma * sigma));
                var sinusoidal = Math.Cos(2 * Math.PI * frequency * xTheta);
                kernel.Set(y, x, gaussian * sinusoidal);
            }
        }

        return kernel;
    }
}
