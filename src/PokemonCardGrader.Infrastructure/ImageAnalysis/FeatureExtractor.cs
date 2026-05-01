using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCvSharp;
using PokemonCardGrader.Application.Configuration;
using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Infrastructure.ImageAnalysis;

/// <summary>
/// Phase 14: Extracts a structured feature vector from a normalized card image.
/// Features include edge roughness, corner geometry, surface variance/texture,
/// color histograms, border thickness, and whitening ratios.
/// The output is suitable for ML model input.
/// </summary>
public sealed class FeatureExtractor(
    IOptions<CardAnalysisOptions> options,
    ILogger<FeatureExtractor> logger)
{
    private readonly CardAnalysisOptions _opts = options.Value;

    /// <summary>
    /// Extracts all features from a normalized card image.
    /// </summary>
    public CardFeatures Extract(Mat normalized, CardRegions regions, CenteringMeasurement? centering)
    {
        using var gray = new Mat();
        Cv2.CvtColor(normalized, gray, ColorConversionCodes.BGR2GRAY);

        var edgeRoughness = ExtractEdgeRoughness(gray, regions);
        var cornerGeometry = ExtractCornerGeometry(gray, regions);
        var surfaceVariance = ExtractSurfaceVariance(gray, regions);
        var surfaceTexture = ExtractSurfaceTexture(gray, regions);
        var colorHistogram = ExtractColorHistogram(normalized);
        var borderThickness = ExtractBorderThickness(gray, normalized.Width, normalized.Height);
        var sharpness = MeasureSharpness(gray);
        var centeringDeviation = ExtractCenteringDeviation(centering);
        var cornerWhitening = ExtractCornerWhitening(gray, regions);
        var edgeWhitening = ExtractEdgeWhitening(gray, regions);

        logger.LogDebug(
            "Features extracted: edges={EdgeCount} corners={CornerCount} " +
            "surfVar={SurfVarCount} surfTex={SurfTexCount} color={ColorCount}",
            edgeRoughness.Length, cornerGeometry.Length,
            surfaceVariance.Length, surfaceTexture.Length, colorHistogram.Length);

        return new CardFeatures
        {
            EdgeRoughness = edgeRoughness,
            CornerGeometry = cornerGeometry,
            SurfaceVariance = surfaceVariance,
            SurfaceTexture = surfaceTexture,
            ColorHistogram = colorHistogram,
            BorderThickness = borderThickness,
            Sharpness = sharpness,
            CenteringDeviation = centeringDeviation,
            CornerWhitening = cornerWhitening,
            EdgeWhitening = edgeWhitening
        };
    }

    private double[] ExtractEdgeRoughness(Mat gray, CardRegions regions)
    {
        var roughness = new double[4]; // Top, Right, Bottom, Left

        for (var i = 0; i < 4; i++)
        {
            var edgeZone = regions.EdgeZones[i];
            using var region = RegionSegmenter.ExtractRegion(gray, edgeZone);

            // Compute gradient magnitude profile along the edge
            using var sobelX = new Mat();
            using var sobelY = new Mat();
            Cv2.Sobel(region, sobelX, MatType.CV_64F, 1, 0, ksize: 3);
            Cv2.Sobel(region, sobelY, MatType.CV_64F, 0, 1, ksize: 3);

            using var magnitude = new Mat();
            Cv2.Magnitude(sobelX, sobelY, magnitude);

            Cv2.MeanStdDev(magnitude, out var mean, out var stddev);

            // Roughness = stddev of gradient (smooth edges have low variance)
            roughness[i] = Math.Round(stddev.Val0, 4);
        }

        return roughness;
    }

    private double[] ExtractCornerGeometry(Mat gray, CardRegions regions)
    {
        var geometry = new double[4]; // TL, TR, BR, BL

        for (var i = 0; i < 4; i++)
        {
            var cornerZone = regions.CornerZones[i];
            using var region = RegionSegmenter.ExtractRegion(gray, cornerZone);

            using var edges = new Mat();
            Cv2.Canny(region, edges, 50, 150);

            Cv2.FindContours(edges, out var contours, out _, RetrievalModes.External,
                ContourApproximationModes.ApproxNone);

            if (contours.Length == 0)
            {
                geometry[i] = 0;
                continue;
            }

            var allPoints = contours.SelectMany(c => c).ToArray();
            if (allPoints.Length < 5)
            {
                geometry[i] = 0;
                continue;
            }

            Cv2.MinEnclosingCircle(allPoints, out _, out var radius);
            var deviation = Math.Abs(radius - _opts.ExpectedCornerRadius);
            geometry[i] = Math.Round(deviation, 4);
        }

        return geometry;
    }

    private double[] ExtractSurfaceVariance(Mat gray, CardRegions regions)
    {
        var gridSize = _opts.FeatureGridSize;
        var inner = regions.InnerRegion;
        using var innerRegion = RegionSegmenter.ExtractRegion(gray, inner);

        var cellW = innerRegion.Cols / gridSize;
        var cellH = innerRegion.Rows / gridSize;
        var variances = new double[gridSize * gridSize];

        for (var row = 0; row < gridSize; row++)
        {
            for (var col = 0; col < gridSize; col++)
            {
                var rect = new Rect(col * cellW, row * cellH,
                    Math.Min(cellW, innerRegion.Cols - col * cellW),
                    Math.Min(cellH, innerRegion.Rows - row * cellH));

                if (rect.Width <= 0 || rect.Height <= 0)
                {
                    variances[row * gridSize + col] = 0;
                    continue;
                }

                using var cell = new Mat(innerRegion, rect);
                Cv2.MeanStdDev(cell, out _, out var stddev);
                variances[row * gridSize + col] = Math.Round(stddev.Val0 * stddev.Val0, 4);
            }
        }

        return variances;
    }

    private double[] ExtractSurfaceTexture(Mat gray, CardRegions regions)
    {
        var gridSize = _opts.FeatureGridSize;
        var inner = regions.InnerRegion;
        using var innerRegion = RegionSegmenter.ExtractRegion(gray, inner);

        var cellW = innerRegion.Cols / gridSize;
        var cellH = innerRegion.Rows / gridSize;
        var textures = new double[gridSize * gridSize];

        for (var row = 0; row < gridSize; row++)
        {
            for (var col = 0; col < gridSize; col++)
            {
                var rect = new Rect(col * cellW, row * cellH,
                    Math.Min(cellW, innerRegion.Cols - col * cellW),
                    Math.Min(cellH, innerRegion.Rows - row * cellH));

                if (rect.Width <= 0 || rect.Height <= 0)
                {
                    textures[row * gridSize + col] = 0;
                    continue;
                }

                using var cell = new Mat(innerRegion, rect);
                using var laplacian = new Mat();
                Cv2.Laplacian(cell, laplacian, MatType.CV_64F);

                Cv2.MeanStdDev(laplacian, out _, out var stddev);
                textures[row * gridSize + col] = Math.Round(stddev.Val0, 4);
            }
        }

        return textures;
    }

    private double[] ExtractColorHistogram(Mat bgr)
    {
        // Convert to HSV and compute histograms
        using var hsv = new Mat();
        Cv2.CvtColor(bgr, hsv, ColorConversionCodes.BGR2HSV);

        var bins = _opts.ColorHistogramBins;
        var histogram = new double[bins * 3]; // H, S, V channels

        for (var ch = 0; ch < 3; ch++)
        {
            using var channel = new Mat();
            Cv2.ExtractChannel(hsv, channel, ch);

            using var hist = new Mat();
            var hdims = new[] { bins };
            var range = ch == 0
                ? new[] { new Rangef(0, 180) }  // Hue range
                : new[] { new Rangef(0, 256) };  // Saturation/Value range

            Cv2.CalcHist([channel], [0], null, hist, 1, hdims, range);

            // Normalize histogram
            var total = channel.Rows * channel.Cols;
            for (var b = 0; b < bins; b++)
            {
                histogram[ch * bins + b] = Math.Round(hist.At<float>(b) / total, 6);
            }
        }

        return histogram;
    }

    private double[] ExtractBorderThickness(Mat gray, int width, int height)
    {
        // Measure border thickness per side using edge detection
        using var edges = new Mat();
        Cv2.Canny(gray, edges, 50, 150);

        var thickness = new double[4]; // Top, Right, Bottom, Left

        // Top border: scan down from top to find first strong horizontal edge
        thickness[0] = FindBorderEdge(edges, width, height, Side.Top);
        thickness[1] = FindBorderEdge(edges, width, height, Side.Right);
        thickness[2] = FindBorderEdge(edges, width, height, Side.Bottom);
        thickness[3] = FindBorderEdge(edges, width, height, Side.Left);

        return thickness;
    }

    private static double FindBorderEdge(Mat edges, int width, int height, Side side)
    {
        var sampleCount = 20;
        var positions = new List<int>();

        for (var s = 0; s < sampleCount; s++)
        {
            var t = 0.2 + 0.6 * s / (sampleCount - 1);

            switch (side)
            {
                case Side.Top:
                {
                    var x = (int)(width * t);
                    for (var y = 0; y < height / 4; y++)
                    {
                        if (x < edges.Cols && y < edges.Rows && edges.At<byte>(y, x) > 0)
                        {
                            positions.Add(y);
                            break;
                        }
                    }
                    break;
                }
                case Side.Bottom:
                {
                    var x = (int)(width * t);
                    for (var y = height - 1; y > height * 3 / 4; y--)
                    {
                        if (x < edges.Cols && y >= 0 && y < edges.Rows && edges.At<byte>(y, x) > 0)
                        {
                            positions.Add(height - 1 - y);
                            break;
                        }
                    }
                    break;
                }
                case Side.Left:
                {
                    var y = (int)(height * t);
                    for (var x = 0; x < width / 4; x++)
                    {
                        if (y < edges.Rows && x < edges.Cols && edges.At<byte>(y, x) > 0)
                        {
                            positions.Add(x);
                            break;
                        }
                    }
                    break;
                }
                case Side.Right:
                {
                    var y = (int)(height * t);
                    for (var x = width - 1; x > width * 3 / 4; x--)
                    {
                        if (y < edges.Rows && x >= 0 && x < edges.Cols && edges.At<byte>(y, x) > 0)
                        {
                            positions.Add(width - 1 - x);
                            break;
                        }
                    }
                    break;
                }
            }
        }

        if (positions.Count == 0) return 0;

        positions.Sort();
        var median = positions[positions.Count / 2];
        var dimension = side is Side.Top or Side.Bottom ? height : width;
        return Math.Round((double)median / dimension, 4);
    }

    private static double MeasureSharpness(Mat gray)
    {
        using var laplacian = new Mat();
        Cv2.Laplacian(gray, laplacian, MatType.CV_64F);
        Cv2.MeanStdDev(laplacian, out _, out var stddev);
        return Math.Round(stddev.Val0 * stddev.Val0, 4);
    }

    private static double[] ExtractCenteringDeviation(CenteringMeasurement? centering)
    {
        if (centering is null)
            return [0, 0];

        return
        [
            Math.Round(Math.Abs(centering.LeftRightFront - 50), 2),
            Math.Round(Math.Abs(centering.TopBottomFront - 50), 2)
        ];
    }

    private double[] ExtractCornerWhitening(Mat gray, CardRegions regions)
    {
        var whitening = new double[4];

        for (var i = 0; i < 4; i++)
        {
            var cornerZone = regions.CornerZones[i];
            using var region = RegionSegmenter.ExtractRegion(gray, cornerZone);

            using var thresh = new Mat();
            Cv2.Threshold(region, thresh, _opts.CornerWhiteThreshold, 255, ThresholdTypes.Binary);
            var whitePixels = Cv2.CountNonZero(thresh);
            var total = region.Rows * region.Cols;
            whitening[i] = Math.Round(total > 0 ? (double)whitePixels / total : 0, 4);
        }

        return whitening;
    }

    private double[] ExtractEdgeWhitening(Mat gray, CardRegions regions)
    {
        var whitening = new double[4];

        for (var i = 0; i < 4; i++)
        {
            var edgeZone = regions.EdgeZones[i];
            using var region = RegionSegmenter.ExtractRegion(gray, edgeZone);

            // Use adaptive threshold for edge whitening
            var mean = Cv2.Mean(region).Val0;
            var threshold = mean < 180 ? _opts.EdgeWhiteThresholdDark : _opts.EdgeWhiteThresholdLight;

            using var thresh = new Mat();
            Cv2.Threshold(region, thresh, threshold, 255, ThresholdTypes.Binary);
            var whitePixels = Cv2.CountNonZero(thresh);
            var total = region.Rows * region.Cols;
            whitening[i] = Math.Round(total > 0 ? (double)whitePixels / total : 0, 4);
        }

        return whitening;
    }

    private enum Side { Top, Right, Bottom, Left }
}
