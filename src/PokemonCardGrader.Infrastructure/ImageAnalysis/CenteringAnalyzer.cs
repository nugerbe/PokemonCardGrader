using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCvSharp;
using PokemonCardGrader.Application.Configuration;
using PokemonCardGrader.Application.Interfaces;
using PokemonCardGrader.Domain.Enums;
using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Infrastructure.ImageAnalysis;

/// <summary>
/// Measures card centering by detecting inner border lines on each side of the normalized card.
/// Uses multiple edge-detection strategies (Canny sweep, adaptive threshold, Sobel gradient)
/// and cross-validates opposite-side pairs. Optionally blends with a learned border prior.
/// </summary>
public sealed class CenteringAnalyzer(
    IOptions<CardAnalysisOptions> options,
    ILogger<CenteringAnalyzer> logger)
{
    private readonly CardAnalysisOptions _opts = options.Value;

    public record CenteringResult(CenteringMeasurement? Centering, BorderLines? DetectedBorders);

    /// <summary>
    /// Measures centering from the normalized card image.
    /// Returns centering percentages and detected border positions.
    /// </summary>
    public CenteringResult Measure(Mat normalized, ImageType imageType, BorderPrior? prior = null)
    {
        var w = _opts.NormalizedWidth;
        var h = _opts.NormalizedHeight;

        using var gray = new Mat();
        Cv2.CvtColor(normalized, gray, ColorConversionCodes.BGR2GRAY);

        var edgeMaps = new List<Mat>();
        try
        {
            // Build multiple edge maps
            foreach (var pair in _opts.CenteringCannyThresholds)
            {
                var e = new Mat();
                Cv2.Canny(gray, e, pair[0], pair[1]);
                edgeMaps.Add(e);
            }

            // Adaptive threshold
            using var blurred = new Mat();
            Cv2.GaussianBlur(gray, blurred, new Size(5, 5), 0);
            var adaptiveEdge = new Mat();
            Cv2.AdaptiveThreshold(blurred, adaptiveEdge, 255, AdaptiveThresholdTypes.GaussianC,
                ThresholdTypes.Binary, blockSize: 31, c: 8);
            Cv2.BitwiseNot(adaptiveEdge, adaptiveEdge);
            edgeMaps.Add(adaptiveEdge);

            // Gradient magnitude (Sobel)
            using var sobelX = new Mat();
            using var sobelY = new Mat();
            Cv2.Sobel(gray, sobelX, MatType.CV_16S, 1, 0);
            Cv2.Sobel(gray, sobelY, MatType.CV_16S, 0, 1);
            using var absX = new Mat();
            using var absY = new Mat();
            Cv2.ConvertScaleAbs(sobelX, absX);
            Cv2.ConvertScaleAbs(sobelY, absY);
            var gradMag = new Mat();
            Cv2.AddWeighted(absX, 0.5, absY, 0.5, 0, gradMag);
            Cv2.Threshold(gradMag, gradMag, 30, 255, ThresholdTypes.Binary);
            edgeMaps.Add(gradMag);

            // Collect border candidates from all edge maps
            var leftCandidates = CollectBorderCandidates(edgeMaps, Side.Left, w, h);
            var rightCandidates = CollectBorderCandidates(edgeMaps, Side.Right, w, h);
            var topCandidates = CollectBorderCandidates(edgeMaps, Side.Top, w, h);
            var bottomCandidates = CollectBorderCandidates(edgeMaps, Side.Bottom, w, h);

            var (leftBorder, rightBorder) = PickBestPair(leftCandidates, rightCandidates, w);
            var (topBorder, bottomBorder) = PickBestPair(topCandidates, bottomCandidates, h);

            // Fallback to prior or default when detection fails
            var anyFailed = leftBorder < 0 || rightBorder < 0 || topBorder < 0 || bottomBorder < 0;
            if (anyFailed)
            {
                logger.LogWarning(
                    "Could not detect all border lines. L={Left} R={Right} T={Top} B={Bottom} ImageType={ImageType} HasPrior={HasPrior}",
                    leftBorder, rightBorder, topBorder, bottomBorder, imageType, prior is not null);

                if (prior is not null)
                {
                    if (leftBorder < 0) leftBorder = (int)(prior.MedianLeft * w);
                    if (rightBorder < 0) rightBorder = (int)((1.0 - prior.MedianRight) * w);
                    if (topBorder < 0) topBorder = (int)(prior.MedianTop * h);
                    if (bottomBorder < 0) bottomBorder = (int)((1.0 - prior.MedianBottom) * h);
                }
                else
                {
                    if (leftBorder < 0) leftBorder = (int)(w * _opts.DefaultBorderFallback);
                    if (rightBorder < 0) rightBorder = (int)(w * _opts.DefaultBorderFallback);
                    if (topBorder < 0) topBorder = (int)(h * _opts.DefaultBorderFallback);
                    if (bottomBorder < 0) bottomBorder = (int)(h * _opts.DefaultBorderFallback);
                }
            }

            // Convert to card-space fractions
            var detectedBorders = new BorderLines
            {
                LeftBorderX = Math.Round(leftBorder / (double)w, 4),
                RightBorderX = Math.Round(1.0 - rightBorder / (double)w, 4),
                TopBorderY = Math.Round(topBorder / (double)h, 4),
                BottomBorderY = Math.Round(1.0 - bottomBorder / (double)h, 4)
            };

            // Blend with prior when both CV and prior succeeded
            if (prior is not null && !anyFailed)
            {
                var priorWeight = Math.Clamp(prior.Confidence * 0.3, 0.0, _opts.MaxPriorBlendWeight);
                var cvWeight = 1.0 - priorWeight;

                detectedBorders = new BorderLines
                {
                    LeftBorderX = Math.Round(detectedBorders.LeftBorderX * cvWeight + prior.MedianLeft * priorWeight, 4),
                    RightBorderX = Math.Round(detectedBorders.RightBorderX * cvWeight + prior.MedianRight * priorWeight, 4),
                    TopBorderY = Math.Round(detectedBorders.TopBorderY * cvWeight + prior.MedianTop * priorWeight, 4),
                    BottomBorderY = Math.Round(detectedBorders.BottomBorderY * cvWeight + prior.MedianBottom * priorWeight, 4)
                };

                leftBorder = (int)(detectedBorders.LeftBorderX * w);
                rightBorder = (int)((1.0 - detectedBorders.RightBorderX) * w);
                topBorder = (int)(detectedBorders.TopBorderY * h);
                bottomBorder = (int)((1.0 - detectedBorders.BottomBorderY) * h);

                logger.LogDebug(
                    "Blended borders with prior (weight={Weight:F2}): L={L:F4} R={R:F4} T={T:F4} B={B:F4}",
                    priorWeight, detectedBorders.LeftBorderX, detectedBorders.RightBorderX,
                    detectedBorders.TopBorderY, detectedBorders.BottomBorderY);
            }

            var totalHorizontal = (double)(leftBorder + rightBorder);
            var totalVertical = (double)(topBorder + bottomBorder);

            double lr = 50, tb = 50;
            if (totalHorizontal > 0) lr = (leftBorder / totalHorizontal) * 100.0;
            if (totalVertical > 0) tb = (topBorder / totalVertical) * 100.0;

            logger.LogInformation(
                "MeasureCentering: L={Left}px R={Right}px T={Top}px B={Bottom}px → LR={LR:F1}% TB={TB:F1}% " +
                "Borders=[{BL:F4},{BR:F4},{BT:F4},{BB:F4}] ImageType={ImageType}",
                leftBorder, rightBorder, topBorder, bottomBorder, lr, tb,
                detectedBorders.LeftBorderX, detectedBorders.RightBorderX,
                detectedBorders.TopBorderY, detectedBorders.BottomBorderY, imageType);

            var centering = new CenteringMeasurement
            {
                LeftRightFront = Math.Round(lr, 1),
                TopBottomFront = Math.Round(tb, 1),
                LeftRightBack = 50,
                TopBottomBack = 50
            };

            return new CenteringResult(centering, detectedBorders);
        }
        finally
        {
            foreach (var map in edgeMaps)
                map.Dispose();
        }
    }

    // ── Border candidate collection ──

    private List<(int Position, double Density)> CollectBorderCandidates(
        List<Mat> edgeMaps, Side side, int cardWidth, int cardHeight)
    {
        var allPeaks = new Dictionary<int, double>();

        foreach (var edgeMap in edgeMaps)
        {
            var densities = ComputeEdgeDensityProfile(edgeMap, side, cardWidth, cardHeight);
            foreach (var (pos, density) in densities)
            {
                if (density > _opts.MinEdgeDensity && (!allPeaks.TryGetValue(pos, out var existing) || density > existing))
                    allPeaks[pos] = density;
            }
        }

        // Find local peaks
        var candidates = new List<(int Position, double Density)>();
        var positions = allPeaks.Keys.OrderBy(p => p).ToList();

        foreach (var pos in positions)
        {
            var density = allPeaks[pos];
            var isLocalPeak = true;

            for (var offset = -5; offset <= 5; offset++)
            {
                if (offset == 0) continue;
                if (allPeaks.TryGetValue(pos + offset, out var neighbourDensity) && neighbourDensity > density)
                {
                    isLocalPeak = false;
                    break;
                }
            }

            if (isLocalPeak)
                candidates.Add((pos, density));
        }

        if (candidates.Count == 0 && allPeaks.Count > 0)
        {
            var best = allPeaks.MaxBy(kvp => kvp.Value);
            candidates.Add((best.Key, best.Value));
        }

        return candidates.OrderByDescending(c => c.Density).ToList();
    }

    private List<(int Position, double Density)> ComputeEdgeDensityProfile(
        Mat edges, Side side, int cardWidth, int cardHeight)
    {
        int scanSize, perpSize;

        switch (side)
        {
            case Side.Left:
            case Side.Right:
                scanSize = cardWidth;
                perpSize = cardHeight;
                break;
            case Side.Top:
            case Side.Bottom:
                scanSize = cardHeight;
                perpSize = cardWidth;
                break;
            default:
                return [];
        }

        var minScan = (int)(scanSize * _opts.MinBorderFraction);
        var maxScan = (int)(scanSize * _opts.MaxBorderFraction);

        var sliceCentres = new[] { 0.15, 0.30, 0.50, 0.70, 0.85 };
        var sliceHalfWidth = (int)(perpSize * 0.06);

        var results = new List<(int Position, double Density)>();

        for (var i = minScan; i <= maxScan; i++)
        {
            var totalEdge = 0;
            var totalSampled = 0;

            foreach (var centre in sliceCentres)
            {
                var sliceStart = Math.Max(0, (int)(perpSize * centre) - sliceHalfWidth);
                var sliceEnd = Math.Min(perpSize, (int)(perpSize * centre) + sliceHalfWidth);

                for (var j = sliceStart; j < sliceEnd; j++)
                {
                    int row, col;
                    switch (side)
                    {
                        case Side.Left: row = j; col = i; break;
                        case Side.Right: row = j; col = scanSize - 1 - i; break;
                        case Side.Top: row = i; col = j; break;
                        case Side.Bottom: row = scanSize - 1 - i; col = j; break;
                        default: continue;
                    }

                    if (row >= 0 && row < edges.Rows && col >= 0 && col < edges.Cols)
                    {
                        totalSampled++;
                        if (edges.At<byte>(row, col) > 0)
                            totalEdge++;
                    }
                }
            }

            if (totalSampled > 0)
                results.Add((i, (double)totalEdge / totalSampled));
        }

        return results;
    }

    private static (int SideA, int SideB) PickBestPair(
        List<(int Position, double Density)> candidatesA,
        List<(int Position, double Density)> candidatesB,
        int axisSize)
    {
        if (candidatesA.Count == 0 && candidatesB.Count == 0)
            return (-1, -1);

        if (candidatesA.Count == 0)
        {
            var best = candidatesB[0];
            return (best.Position, best.Position);
        }

        if (candidatesB.Count == 0)
        {
            var best = candidatesA[0];
            return (best.Position, best.Position);
        }

        var bestScore = double.MinValue;
        var bestA = candidatesA[0].Position;
        var bestB = candidatesB[0].Position;

        var topA = candidatesA.Take(5).ToList();
        var topB = candidatesB.Take(5).ToList();

        foreach (var (posA, densA) in topA)
        {
            foreach (var (posB, densB) in topB)
            {
                var densityScore = Math.Sqrt(densA * densB);

                var fracA = (double)posA / axisSize;
                var fracB = (double)posB / axisSize;
                var minFrac = Math.Min(fracA, fracB);
                var maxFrac = Math.Max(fracA, fracB);
                var symmetryRatio = maxFrac > 0.001 ? minFrac / maxFrac : 0;

                var avgFrac = (fracA + fracB) / 2.0;
                var reasonableness = avgFrac switch
                {
                    >= 0.03 and <= 0.08 => 1.0,
                    >= 0.02 and < 0.03 => 0.8,
                    > 0.08 and <= 0.12 => 0.8,
                    > 0.12 => 0.5,
                    _ => 0.6
                };

                var score = densityScore * 0.65
                          + symmetryRatio * 0.15
                          + reasonableness * 0.2;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestA = posA;
                    bestB = posB;
                }
            }
        }

        return (bestA, bestB);
    }

    private enum Side { Left, Right, Top, Bottom }
}
