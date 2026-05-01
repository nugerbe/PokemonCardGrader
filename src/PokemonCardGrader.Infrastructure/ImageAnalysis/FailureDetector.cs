using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCvSharp;
using PokemonCardGrader.Application.Configuration;
using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Infrastructure.ImageAnalysis;

/// <summary>
/// Detects failure/invalid scenarios that should block or qualify grading:
/// multiple cards, occluded cards, non-card objects, sleeved/top-loaded cards.
/// Runs after card detection to validate the detection result.
/// </summary>
public sealed class FailureDetector(
    IOptions<CardAnalysisOptions> options,
    ILogger<FailureDetector> logger)
{
    private readonly CardAnalysisOptions _opts = options.Value;

    public FailureDetectionResult Detect(
        Mat src, Point2f[]? primaryQuad, bool usedFallback)
    {
        var cardCount = CountCardLikeObjects(src, primaryQuad);
        var isSleeved = DetectSleeveOrTopLoader(src, primaryQuad);
        var (isOccluded, visibleFraction) = DetectOcclusion(src, primaryQuad);

        string? failureType = null;
        string? description = null;
        var hasBlocking = false;
        var confidence = 1.0;

        // Check for multiple cards
        if (cardCount > _opts.MaxAllowedCards)
        {
            failureType = "multiple_cards";
            description = $"Detected {cardCount} card-like objects. Please submit a single card.";
            hasBlocking = true;
            confidence = 0.85;
            logger.LogWarning("Multiple cards detected: {Count}", cardCount);
        }
        // Check for no card detected
        else if (primaryQuad is null && !usedFallback)
        {
            failureType = "no_card_detected";
            description = "Could not detect a card in the image. Please ensure the card is clearly visible.";
            hasBlocking = true;
            confidence = 0.9;
            logger.LogWarning("No card detected in image.");
        }
        // Occlusion is now a soft warning — the card detector's quad-scoring
        // (aspect ratio, area, convexity, parallelism, side-length consistency)
        // already encodes "is this a plausible card?" at score selection time.
        // A successful high-score detection means we have a quad worth analysing
        // even if the card sits in a small portion of the frame. We let analysis
        // proceed and surface lower confidence rather than rejecting outright.
        else if (isOccluded && visibleFraction < _opts.MinVisibleAreaFraction)
        {
            // Confidence drops with how partial the detection looks. Heavy
            // occlusion (visibleFraction < 0.4) drops confidence to 0.5;
            // mild occlusion stays around 0.8.
            confidence = Math.Clamp(0.5 + visibleFraction * 0.4, 0.5, 0.9);
            logger.LogInformation(
                "Card detection looks partial ({Fraction:P0} visible) — proceeding with reduced confidence ({Conf:F2}).",
                visibleFraction, confidence);
        }
        // Sleeve/top-loader is a warning, not blocking
        else if (isSleeved)
        {
            logger.LogInformation("Sleeve or top-loader detected — grading will proceed with reduced confidence.");
            confidence = 0.8;
        }

        return new FailureDetectionResult
        {
            HasBlockingFailure = hasBlocking,
            FailureType = failureType,
            Confidence = confidence,
            Description = description,
            DetectedCardCount = cardCount,
            IsSleevedOrTopLoaded = isSleeved,
            IsOccluded = isOccluded,
            VisibleAreaFraction = visibleFraction
        };
    }

    private int CountCardLikeObjects(Mat src, Point2f[]? primaryQuad)
    {
        using var gray = new Mat();
        Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);

        using var blurred = new Mat();
        Cv2.GaussianBlur(gray, blurred, new Size(5, 5), 0);

        using var edges = new Mat();
        Cv2.Canny(blurred, edges, 50, 150);

        using var closed = new Mat();
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(5, 5));
        Cv2.MorphologyEx(edges, closed, MorphTypes.Close, kernel, iterations: 3);

        Cv2.FindContours(closed, out var contours, out _, RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);

        var imageArea = src.Width * src.Height;
        var cardLikeCount = 0;

        foreach (var contour in contours)
        {
            var area = Cv2.ContourArea(contour);
            var areaFraction = area / imageArea;

            // Must be at least 5% of the image to be card-like
            if (areaFraction < 0.05) continue;

            var peri = Cv2.ArcLength(contour, true);
            var approx = Cv2.ApproxPolyDP(contour, 0.04 * peri, true);

            if (approx.Length != 4) continue;

            // Check aspect ratio
            var rect = Cv2.MinAreaRect(contour);
            var aspect = Math.Min(rect.Size.Width, rect.Size.Height) /
                         Math.Max(rect.Size.Width, rect.Size.Height);

            if (Math.Abs(aspect - _opts.CardAspectRatio) < 0.15)
                cardLikeCount++;
        }

        // At minimum, count the primary detection
        return Math.Max(cardLikeCount, primaryQuad is not null ? 1 : 0);
    }

    private bool DetectSleeveOrTopLoader(Mat src, Point2f[]? quad)
    {
        if (quad is null) return false;

        using var gray = new Mat();
        Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);

        // Check for reflectance patterns along the card edges
        // Sleeves and top-loaders create consistent bright edges
        var edgeReflectance = MeasureEdgeReflectance(gray, quad, src.Width, src.Height);

        if (edgeReflectance > _opts.SleeveReflectanceThreshold)
        {
            logger.LogInformation("High edge reflectance ({Reflectance:F3}) suggests sleeve/top-loader",
                edgeReflectance);
            return true;
        }

        // Check for double-edge pattern (card edge inside sleeve edge)
        var hasDoubleEdge = DetectDoubleEdgePattern(gray, quad);
        if (hasDoubleEdge)
        {
            logger.LogInformation("Double-edge pattern suggests sleeve/top-loader");
            return true;
        }

        return false;
    }

    private double MeasureEdgeReflectance(Mat gray, Point2f[] quad, int width, int height)
    {
        // Sample pixels along each edge just outside the card boundary
        var totalReflectance = 0.0;
        var sampleCount = 0;
        var offset = Math.Max(3, Math.Min(width, height) / 100);

        for (var side = 0; side < 4; side++)
        {
            var p1 = quad[side];
            var p2 = quad[(side + 1) % 4];

            // Sample 20 points along each side
            for (var t = 0.1; t <= 0.9; t += 0.04)
            {
                var px = (int)(p1.X + (p2.X - p1.X) * t);
                var py = (int)(p1.Y + (p2.Y - p1.Y) * t);

                // Normal direction (outward from card center)
                var cx = quad.Average(p => p.X);
                var cy = quad.Average(p => p.Y);
                var dx = px - cx;
                var dy = py - cy;
                var len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1) continue;

                var nx = (int)(px + dx / len * offset);
                var ny = (int)(py + dy / len * offset);

                if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;

                var val = gray.At<byte>(ny, nx);
                totalReflectance += val / 255.0;
                sampleCount++;
            }
        }

        return sampleCount > 0 ? totalReflectance / sampleCount : 0;
    }

    private bool DetectDoubleEdgePattern(Mat gray, Point2f[] quad)
    {
        // Look for a second set of edges just outside the detected card boundary
        using var edges = new Mat();
        Cv2.Canny(gray, edges, 50, 150);

        var offset = 15; // pixels outside the detected boundary
        var doubleEdgeVotes = 0;
        var totalSamples = 0;

        for (var side = 0; side < 4; side++)
        {
            var p1 = quad[side];
            var p2 = quad[(side + 1) % 4];

            for (var t = 0.2; t <= 0.8; t += 0.05)
            {
                var px = (int)(p1.X + (p2.X - p1.X) * t);
                var py = (int)(p1.Y + (p2.Y - p1.Y) * t);

                var cx = quad.Average(p => p.X);
                var cy = quad.Average(p => p.Y);
                var dx = px - cx;
                var dy = py - cy;
                var len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1) continue;

                // Check for edge at offset distance outside the card
                var nx = (int)(px + dx / len * offset);
                var ny = (int)(py + dy / len * offset);

                if (nx < 0 || nx >= edges.Cols || ny < 0 || ny >= edges.Rows) continue;

                totalSamples++;
                if (edges.At<byte>(ny, nx) > 0)
                    doubleEdgeVotes++;
            }
        }

        var ratio = totalSamples > 0 ? (double)doubleEdgeVotes / totalSamples : 0;
        return ratio > 0.3; // 30% of sampled points have a second edge
    }

    private (bool IsOccluded, double VisibleFraction) DetectOcclusion(Mat src, Point2f[]? quad)
    {
        if (quad is null) return (false, 0);

        var quadArea = Cv2.ContourArea(quad.Select(p => new Point((int)p.X, (int)p.Y)).ToArray());
        var imageArea = src.Width * src.Height;

        // Phone photos of a card on a desk typically fill 15-50% of the frame.
        // Anchor the heuristic at 25% rather than 50% so a normally-framed
        // photo doesn't get flagged as occluded.
        const double ExpectedFillFraction = 0.25;
        var expectedArea = imageArea * ExpectedFillFraction;

        // Check if detected quad is much smaller than expected.
        // Below 25% of expected (= ~6% of image) is genuinely tiny.
        var areaRatio = quadArea / Math.Max(expectedArea, 1);
        if (areaRatio < 0.25)
        {
            return (true, areaRatio);
        }

        // Check for uniform regions overlapping the card (fingers, objects)
        using var mask = Mat.Zeros(src.Size(), MatType.CV_8UC1);
        var quadPoints = quad.Select(p => new Point((int)p.X, (int)p.Y)).ToArray();
        Cv2.FillConvexPoly(mask, quadPoints, Scalar.White);

        using var gray = new Mat();
        Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);

        // Look for large low-variance regions inside the card area (potential occlusion)
        using var cardRegion = new Mat();
        gray.CopyTo(cardRegion, mask);

        Cv2.MeanStdDev(cardRegion, out _, out var stddev);
        var cardStdDev = stddev.Val0;

        // Very low variance inside card area suggests major occlusion (e.g., a
        // hand covering the entire artwork). Pokémon cards always have rich
        // colour variance from the artwork, so cardStdDev < 12 is a strong signal.
        if (cardStdDev < 12)
        {
            return (true, 0.4);
        }

        // Card detected within reasonable area
        var visibleFraction = Math.Min(1.0, quadArea / (imageArea * ExpectedFillFraction));
        return (visibleFraction < _opts.MinVisibleAreaFraction, visibleFraction);
    }
}
