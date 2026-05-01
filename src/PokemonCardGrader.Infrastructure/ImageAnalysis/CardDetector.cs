using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCvSharp;
using PokemonCardGrader.Application.Configuration;

namespace PokemonCardGrader.Infrastructure.ImageAnalysis;

/// <summary>
/// Detects the card quadrilateral in a source image using multiple strategies:
/// multi-threshold Canny, adaptive threshold, brightness segmentation, and full-frame fallback.
/// Scores candidates on aspect ratio, area, convexity, parallelism, and side-length consistency.
/// </summary>
public sealed class CardDetector(
    IOptions<CardAnalysisOptions> options,
    ILogger<CardDetector> logger)
{
    private readonly CardAnalysisOptions _opts = options.Value;

    /// <summary>Result of card detection, including the quad and whether fallback was used.</summary>
    public sealed record DetectionResult(Point2f[] Quad, bool UsedFullFrameFallback);

    /// <summary>
    /// Finds the best card quadrilateral in the source image.
    /// Returns 4 ordered corners [TL, TR, BR, BL] or null if no card found.
    /// </summary>
    public Point2f[]? Detect(Mat src) => DetectWithMetadata(src)?.Quad;

    /// <summary>
    /// Finds the best card quadrilateral with detection metadata.
    /// Returns detection result including whether full-frame fallback was used, or null if no card found.
    /// </summary>
    public DetectionResult? DetectWithMetadata(Mat src)
    {
        using var gray = new Mat();
        Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);

        using var blurred = new Mat();
        Cv2.GaussianBlur(gray, blurred, new Size(5, 5), 0);

        var imageArea = src.Width * src.Height;
        var candidates = new List<Point2f[]>();

        // Strategy 1: Multi-threshold Canny sweep with morphological Close
        foreach (var pair in _opts.CannyThresholds)
        {
            var (low, high) = (pair[0], pair[1]);
            var before = candidates.Count;

            using var edges = new Mat();
            Cv2.Canny(blurred, edges, low, high);

            using var closed = new Mat();
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3));
            Cv2.MorphologyEx(edges, closed, MorphTypes.Close, kernel, iterations: 2);

            ExtractQuadCandidates(closed, imageArea, candidates);

            logger.LogInformation("Canny({Low},{High}): {Count} candidates.",
                low, high, candidates.Count - before);
        }

        // Strategy 2: Adaptive threshold
        {
            var before = candidates.Count;

            using var adaptive = new Mat();
            Cv2.AdaptiveThreshold(blurred, adaptive, 255, AdaptiveThresholdTypes.GaussianC,
                ThresholdTypes.Binary, blockSize: 51, c: 5);
            Cv2.BitwiseNot(adaptive, adaptive);

            using var adaptiveClosed = new Mat();
            using var adaptiveKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3));
            Cv2.MorphologyEx(adaptive, adaptiveClosed, MorphTypes.Close, adaptiveKernel, iterations: 2);

            ExtractQuadCandidates(adaptiveClosed, imageArea, candidates);

            logger.LogInformation("Adaptive threshold: {Count} candidates.", candidates.Count - before);
        }

        // Strategy 3: Brightness segmentation (Otsu on HSV V-channel)
        {
            var before = candidates.Count;

            using var hsv = new Mat();
            Cv2.CvtColor(src, hsv, ColorConversionCodes.BGR2HSV);
            using var vChannel = new Mat();
            Cv2.ExtractChannel(hsv, vChannel, 2);

            using var otsu = new Mat();
            Cv2.Threshold(vChannel, otsu, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

            using var otsuClosed = new Mat();
            using var otsuKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(5, 5));
            Cv2.MorphologyEx(otsu, otsuClosed, MorphTypes.Close, otsuKernel, iterations: 3);

            ExtractQuadCandidates(otsuClosed, imageArea, candidates);

            logger.LogInformation("Otsu brightness: {Count} candidates.", candidates.Count - before);
        }

        // Score and select best candidate
        if (candidates.Count > 0)
        {
            var scored = candidates
                .Select(quad => (Quad: quad, Score: ScoreCandidate(quad, src.Width, src.Height)))
                .OrderByDescending(x => x.Score)
                .ToList();

            var (Quad, Score) = scored[0];

            logger.LogInformation(
                "Card detection: {Count} candidates, best score={Score:F3}, top-3 scores=[{Scores}]",
                candidates.Count, Score,
                string.Join(", ", scored.Take(3).Select(s => s.Score.ToString("F3"))));

            return new DetectionResult(Quad, UsedFullFrameFallback: false);
        }

        // Strategy 4: Full-frame fallback
        var frameQuad = TryFullFrameFallback(gray, src.Width, src.Height);
        if (frameQuad is not null)
        {
            logger.LogInformation("Card detection: using full-frame fallback.");
            return new DetectionResult(frameQuad, UsedFullFrameFallback: true);
        }

        logger.LogWarning("Card detection: all strategies produced 0 candidates and full-frame fallback failed.");
        return null;
    }

    private void ExtractQuadCandidates(Mat binaryImage, int imageArea, List<Point2f[]> candidates)
    {
        Cv2.FindContours(binaryImage, out var contours, out _, RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);

        if (contours.Length == 0)
            return;

        var sorted = contours
            .Select(c => (Contour: c, Area: Cv2.ContourArea(c)))
            .OrderByDescending(x => x.Area)
            .ToList();

        var largestAreaPct = sorted[0].Area / imageArea * 100.0;

        var filtered = sorted
            .Where(x => x.Area >= imageArea * _opts.MinContourAreaFraction)
            .Take(_opts.MaxCandidatesPerStrategy)
            .ToList();

        if (filtered.Count == 0)
        {
            logger.LogInformation(
                "ExtractQuadCandidates: {TotalContours} contours found, largest={LargestPct:F1}% of image — all below {Threshold}% area threshold.",
                contours.Length, largestAreaPct, _opts.MinContourAreaFraction * 100);
            return;
        }

        foreach (var (contour, _) in filtered)
        {
            var peri = Cv2.ArcLength(contour, true);
            var found = false;

            // ApproxPolyDP cascade
            foreach (var eps in _opts.ApproxEpsilons)
            {
                var approx = Cv2.ApproxPolyDP(contour, eps * peri, true);
                if (approx.Length == 4 && Cv2.IsContourConvex(approx))
                {
                    candidates.Add(OrderQuadrilateral(approx.Select(p => new Point2f(p.X, p.Y)).ToArray()));
                    found = true;
                    break;
                }
            }

            if (found) continue;

            // Convex hull simplification
            var hull = Cv2.ConvexHull(contour);
            if (hull.Length >= 4)
            {
                var hullPeri = Cv2.ArcLength(hull, true);
                foreach (var eps in _opts.ApproxEpsilons)
                {
                    var approx = Cv2.ApproxPolyDP(hull, eps * hullPeri, true);
                    if (approx.Length == 4)
                    {
                        candidates.Add(OrderQuadrilateral(approx.Select(p => new Point2f(p.X, p.Y)).ToArray()));
                        found = true;
                        break;
                    }
                }
            }

            if (found) continue;

            // MinAreaRect fallback
            var rect = Cv2.MinAreaRect(contour);
            var pts = rect.Points();
            candidates.Add(OrderQuadrilateral(pts.Select(p => new Point2f(p.X, p.Y)).ToArray()));
        }
    }

    internal double ScoreCandidate(Point2f[] quad, int imageWidth, int imageHeight)
    {
        var imageArea = (double)(imageWidth * imageHeight);

        var topLen = Distance(quad[0], quad[1]);
        var rightLen = Distance(quad[1], quad[2]);
        var bottomLen = Distance(quad[2], quad[3]);
        var leftLen = Distance(quad[3], quad[0]);

        var avgWidth = (topLen + bottomLen) / 2.0;
        var avgHeight = (leftLen + rightLen) / 2.0;

        // 1. Aspect ratio similarity
        var candidateAspect = Math.Min(avgWidth, avgHeight) / Math.Max(avgWidth, avgHeight);
        var aspectDiff = Math.Abs(candidateAspect - _opts.CardAspectRatio);
        var aspectScore = Math.Max(0, 1.0 - aspectDiff * 5.0);

        // 2. Area fraction of image
        var quadArea = Cv2.ContourArea(quad.Select(p => new Point((int)p.X, (int)p.Y)).ToArray());
        var areaFraction = quadArea / imageArea;
        double areaScore;
        if (areaFraction >= _opts.IdealAreaMin && areaFraction <= _opts.IdealAreaMax)
            areaScore = 1.0;
        else if (areaFraction < _opts.IdealAreaMin)
            areaScore = areaFraction / _opts.IdealAreaMin;
        else
            areaScore = Math.Max(0, 1.0 - (areaFraction - _opts.IdealAreaMax) * 5.0);

        // 3. Convexity
        var isConvex = Cv2.IsContourConvex(quad.Select(p => new Point((int)p.X, (int)p.Y)).ToArray());
        var convexityScore = isConvex ? 1.0 : 0.3;

        // 4. Edge parallelism
        var topVec = Normalize(new Point2f(quad[1].X - quad[0].X, quad[1].Y - quad[0].Y));
        var bottomVec = Normalize(new Point2f(quad[2].X - quad[3].X, quad[2].Y - quad[3].Y));
        var leftVec = Normalize(new Point2f(quad[3].X - quad[0].X, quad[3].Y - quad[0].Y));
        var rightVec = Normalize(new Point2f(quad[2].X - quad[1].X, quad[2].Y - quad[1].Y));

        var tbParallel = Math.Abs(Dot(topVec, bottomVec));
        var lrParallel = Math.Abs(Dot(leftVec, rightVec));
        var parallelScore = (tbParallel + lrParallel) / 2.0;

        // 5. Opposite-side length consistency
        var widthConsistency = Math.Min(topLen, bottomLen) / Math.Max(topLen, bottomLen);
        var heightConsistency = Math.Min(leftLen, rightLen) / Math.Max(leftLen, rightLen);
        var consistencyScore = (widthConsistency + heightConsistency) / 2.0;

        return (aspectScore * _opts.ScoreWeightAspect)
             + (areaScore * _opts.ScoreWeightArea)
             + (convexityScore * _opts.ScoreWeightConvexity)
             + (parallelScore * _opts.ScoreWeightParallelism)
             + (consistencyScore * _opts.ScoreWeightConsistency);
    }

    private Point2f[]? TryFullFrameFallback(Mat gray, int width, int height)
    {
        var imageAspect = (double)Math.Min(width, height) / Math.Max(width, height);
        var aspectMatch = Math.Abs(imageAspect - _opts.CardAspectRatio) < _opts.FullFrameAspectTolerance;

        var stripSize = Math.Max(5, Math.Min(width, height) / 40);
        var borderBrightness = MeasureBorderBrightness(gray, width, height, stripSize);
        var borderIsBright = borderBrightness > 100;

        if (!aspectMatch && !borderIsBright)
            return null;

        var insetX = width * (float)_opts.FullFrameInsetFraction;
        var insetY = height * (float)_opts.FullFrameInsetFraction;

        return OrderQuadrilateral(
        [
            new Point2f(insetX, insetY),
            new Point2f(width - insetX, insetY),
            new Point2f(width - insetX, height - insetY),
            new Point2f(insetX, height - insetY)
        ]);
    }

    // ── Static helpers ──

    private static double MeasureBorderBrightness(Mat gray, int width, int height, int stripSize)
    {
        double totalMean = 0;
        var count = 0;

        if (stripSize < height)
        {
            using var top = new Mat(gray, new Rect(0, 0, width, stripSize));
            totalMean += Cv2.Mean(top).Val0;
            count++;
        }
        if (stripSize < height)
        {
            using var bottom = new Mat(gray, new Rect(0, height - stripSize, width, stripSize));
            totalMean += Cv2.Mean(bottom).Val0;
            count++;
        }
        if (stripSize < width)
        {
            using var left = new Mat(gray, new Rect(0, 0, stripSize, height));
            totalMean += Cv2.Mean(left).Val0;
            count++;
        }
        if (stripSize < width)
        {
            using var right = new Mat(gray, new Rect(width - stripSize, 0, stripSize, height));
            totalMean += Cv2.Mean(right).Val0;
            count++;
        }

        return count > 0 ? totalMean / count : 0;
    }

    internal static float Distance(Point2f a, Point2f b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    internal static Point2f Normalize(Point2f v)
    {
        var len = MathF.Sqrt(v.X * v.X + v.Y * v.Y);
        return len > 0 ? new Point2f(v.X / len, v.Y / len) : new Point2f(0, 0);
    }

    internal static float Dot(Point2f a, Point2f b) => a.X * b.X + a.Y * b.Y;

    /// <summary>
    /// Orders quadrilateral points as: top-left, top-right, bottom-right, bottom-left.
    /// </summary>
    internal static Point2f[] OrderQuadrilateral(Point2f[] pts)
    {
        var sorted = pts.OrderBy(p => p.X + p.Y).ToArray();
        var tl = sorted[0];
        var br = sorted[3];

        var remaining = sorted[1..3].OrderBy(p => p.Y - p.X).ToArray();
        var tr = remaining[0];
        var bl = remaining[1];

        return [tl, tr, br, bl];
    }
}
