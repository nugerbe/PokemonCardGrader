using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using PokemonCardGrader.Application.Interfaces;
using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Infrastructure.ML;

/// <summary>
/// Learns typical border positions from historical user corrections.
/// Computes a statistical prior (median + confidence) that the image analysis
/// service blends with OpenCV detection to improve initial border placement.
/// Results are cached for 30 minutes to avoid repeated DB queries.
/// </summary>
public sealed class BorderPredictionService(
    ICardSubmissionRepository repository,
    ILogger<BorderPredictionService> logger) : IBorderPredictionService
{
    private BorderPrior? _cached;
    private DateTimeOffset _cacheExpiry = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private const int MinSamplesForPrior = 3;
    private const int MaxSamples = 500;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

    public async Task<BorderPrior?> GetBorderPriorAsync(CancellationToken ct = default)
    {
        if (DateTimeOffset.UtcNow < _cacheExpiry)
            return _cached;

        await _lock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring lock
            if (DateTimeOffset.UtcNow < _cacheExpiry)
                return _cached;

            _cached = await ComputePriorAsync(ct);
            _cacheExpiry = DateTimeOffset.UtcNow + CacheDuration;
            return _cached;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<BorderPrior?> ComputePriorAsync(CancellationToken ct)
    {
        // Border priors are now derived from UserCorrection-source analysis
        // records — each one is the recalculated analysis after the user
        // dragged the border guides into place. The BorderLines on the
        // record's Result are the user-confirmed positions.
        var records = await repository.GetRecentUserCorrectionRecordsAsync(MaxSamples, ct);

        var withBorders = records
            .Where(r => r.Result.Overlay is not null
                && r.Result.Overlay.OuterGuides.Count == 8
                && r.Result.Overlay.InnerGuides.Count == 8)
            .Select(r => DeriveBorderFractions(r.Result.Overlay!))
            .Where(b => b is not null)
            .Select(b => b!)
            .ToList();

        if (withBorders.Count < MinSamplesForPrior)
        {
            logger.LogDebug("Insufficient border corrections for prior: {Count} < {Min}",
                withBorders.Count, MinSamplesForPrior);
            return null;
        }

        var lefts = withBorders.Select(b => b.LeftBorderX).OrderBy(v => v).ToList();
        var rights = withBorders.Select(b => b.RightBorderX).OrderBy(v => v).ToList();
        var tops = withBorders.Select(b => b.TopBorderY).OrderBy(v => v).ToList();
        var bottoms = withBorders.Select(b => b.BottomBorderY).OrderBy(v => v).ToList();

        var medianLeft = Median(lefts);
        var medianRight = Median(rights);
        var medianTop = Median(tops);
        var medianBottom = Median(bottoms);

        // Confidence based on sample count (maxes out at 50 samples)
        var countFactor = Math.Min(withBorders.Count / 50.0, 1.0);
        var confidence = Math.Clamp(countFactor, 0.05, 0.95);

        var prior = new BorderPrior
        {
            MedianLeft = medianLeft,
            MedianRight = medianRight,
            MedianTop = medianTop,
            MedianBottom = medianBottom,
            SampleCount = withBorders.Count,
            Confidence = confidence
        };

        logger.LogInformation(
            "Computed border prior from {Count} corrections: L={Left:F4} R={Right:F4} T={Top:F4} B={Bottom:F4} confidence={Conf:F2}",
            withBorders.Count, medianLeft, medianRight, medianTop, medianBottom, confidence);

        return prior;
    }

    private static double Median(List<double> sorted)
    {
        var n = sorted.Count;
        if (n == 0) return 0;
        return n % 2 == 1
            ? sorted[n / 2]
            : (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
    }

    private static BorderLines? DeriveBorderFractions(AnalysisOverlay overlay)
    {
        var outer = overlay.OuterGuides;
        var inner = overlay.InnerGuides;

        var outerMinX = outer.Min(p => p.X);
        var outerMaxX = outer.Max(p => p.X);
        var outerMinY = outer.Min(p => p.Y);
        var outerMaxY = outer.Max(p => p.Y);
        var outerWidth = outerMaxX - outerMinX;
        var outerHeight = outerMaxY - outerMinY;

        if (outerWidth <= 0 || outerHeight <= 0) return null;

        var innerLeft = (inner[6].X + inner[7].X) / 2.0;
        var innerRight = (inner[2].X + inner[3].X) / 2.0;
        var innerTop = (inner[0].Y + inner[1].Y) / 2.0;
        var innerBottom = (inner[4].Y + inner[5].Y) / 2.0;

        return new BorderLines
        {
            LeftBorderX = (innerLeft - outerMinX) / outerWidth,
            RightBorderX = (innerRight - outerMinX) / outerWidth,
            TopBorderY = (innerTop - outerMinY) / outerHeight,
            BottomBorderY = (innerBottom - outerMinY) / outerHeight
        };
    }
}
