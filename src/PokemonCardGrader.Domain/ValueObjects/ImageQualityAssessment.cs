namespace PokemonCardGrader.Domain.ValueObjects;

/// <summary>
/// Pre-processing gate assessment of input image quality.
/// Evaluates blur, exposure, glare, resolution, and card visibility
/// before the main analysis pipeline runs.
/// </summary>
public sealed record ImageQualityAssessment
{
    /// <summary>Overall quality score 0-1. Below threshold triggers rejection or confidence reduction.</summary>
    public required double OverallScore { get; init; }

    /// <summary>Sharpness score 0-1 based on Laplacian variance. Low values indicate blur.</summary>
    public required double SharpnessScore { get; init; }

    /// <summary>Exposure score 0-1. Penalizes under/over-exposure based on histogram analysis.</summary>
    public required double ExposureScore { get; init; }

    /// <summary>Glare score 0-1. Penalizes large saturated regions that obscure card details.</summary>
    public required double GlareScore { get; init; }

    /// <summary>Resolution adequacy score 0-1. Penalizes images too small for reliable analysis.</summary>
    public required double ResolutionScore { get; init; }

    /// <summary>Noise level score 0-1. High noise degrades defect and surface detection.</summary>
    public required double NoiseScore { get; init; }

    /// <summary>Whether the image passed the quality gate (acceptable for grading).</summary>
    public required bool PassedGate { get; init; }

    /// <summary>Human-readable issues found during quality assessment.</summary>
    public required List<string> Issues { get; init; }

    /// <summary>Recommended action: "proceed", "reduce_confidence", or "reject".</summary>
    public required string RecommendedAction { get; init; }
}
