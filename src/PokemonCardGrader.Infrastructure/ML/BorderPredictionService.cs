using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using PokemonCardGrader.Application.Interfaces;

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
        var corrections = await repository.GetCorrectionsWithAdjustedBordersAsync(MaxSamples, ct);

        // Filter to only corrections where the user actually adjusted borders
        var withBorders = corrections
            .Where(c => c.Correction.AdjustedBorders is not null)
            .Select(c => c.Correction.AdjustedBorders!)
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

        // Confidence based on sample count and consistency (low variance = high confidence)
        var countFactor = Math.Min(withBorders.Count / 50.0, 1.0); // maxes out at 50 samples
        var varianceFactor = 1.0 - Math.Min(
            (Variance(lefts) + Variance(rights) + Variance(tops) + Variance(bottoms)) / 4.0
            / 0.01, // normalize: variance of 0.01 (1% of card) → factor goes to 0
            1.0);
        var confidence = Math.Clamp(countFactor * 0.6 + varianceFactor * 0.4, 0.05, 0.95);

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

    private static double Variance(List<double> values)
    {
        if (values.Count < 2) return 0;
        var mean = values.Average();
        return values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1);
    }
}
