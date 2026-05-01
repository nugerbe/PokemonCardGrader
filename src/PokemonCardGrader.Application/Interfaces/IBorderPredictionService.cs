using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Application.Interfaces;

/// <summary>
/// Learns typical border positions from user corrections and provides
/// statistical priors that improve initial border detection over time.
/// </summary>
public interface IBorderPredictionService
{
    /// <summary>
    /// Returns a learned border prior computed from historical user corrections,
    /// or null if insufficient data exists.
    /// </summary>
    Task<BorderPrior?> GetBorderPriorAsync(CancellationToken ct = default);
}

/// <summary>
/// Statistical prior for border positions learned from user corrections.
/// All positions are normalized 0-1 relative to card dimensions.
/// </summary>
public sealed record BorderPrior
{
    /// <summary>Median left border position (fraction from left edge).</summary>
    public required double MedianLeft { get; init; }

    /// <summary>Median right border position (fraction from left edge, e.g. 0.95).</summary>
    public required double MedianRight { get; init; }

    /// <summary>Median top border position (fraction from top edge).</summary>
    public required double MedianTop { get; init; }

    /// <summary>Median bottom border position (fraction from top edge, e.g. 0.95).</summary>
    public required double MedianBottom { get; init; }

    /// <summary>Number of correction samples used to compute this prior.</summary>
    public required int SampleCount { get; init; }

    /// <summary>
    /// Overall confidence in the prior (0-1). Based on sample count and
    /// consistency of corrections (low variance = high confidence).
    /// </summary>
    public required double Confidence { get; init; }
}
