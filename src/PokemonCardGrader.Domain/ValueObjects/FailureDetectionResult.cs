namespace PokemonCardGrader.Domain.ValueObjects;

/// <summary>
/// Result of failure/invalid-scenario detection.
/// Identifies conditions that should block or qualify the grading process.
/// </summary>
public sealed record FailureDetectionResult
{
    /// <summary>Whether any blocking failure was detected.</summary>
    public required bool HasBlockingFailure { get; init; }

    /// <summary>Specific failure type detected, or null if none.</summary>
    public required string? FailureType { get; init; }

    /// <summary>Confidence in the failure detection (0-1).</summary>
    public required double Confidence { get; init; }

    /// <summary>Human-readable description of the failure.</summary>
    public required string? Description { get; init; }

    /// <summary>Number of card-like objects detected in the image.</summary>
    public required int DetectedCardCount { get; init; }

    /// <summary>Whether the card appears to be in a sleeve or top-loader.</summary>
    public required bool IsSleevedOrTopLoaded { get; init; }

    /// <summary>Whether significant occlusion was detected.</summary>
    public required bool IsOccluded { get; init; }

    /// <summary>Fraction of the card area that is visible (0-1).</summary>
    public required double VisibleAreaFraction { get; init; }

    public static FailureDetectionResult None => new()
    {
        HasBlockingFailure = false,
        FailureType = null,
        Confidence = 1.0,
        Description = null,
        DetectedCardCount = 1,
        IsSleevedOrTopLoaded = false,
        IsOccluded = false,
        VisibleAreaFraction = 1.0
    };
}
