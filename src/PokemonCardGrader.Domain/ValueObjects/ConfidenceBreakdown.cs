namespace PokemonCardGrader.Domain.ValueObjects;

/// <summary>
/// Detailed breakdown of analysis pipeline confidence.
/// Each component is scored 0.0 to 1.0.
/// </summary>
public sealed record ConfidenceBreakdown
{
    /// <summary>Input image quality (sharpness, exposure, noise). 0 = unusable, 1 = ideal.</summary>
    public required double ImageQuality { get; init; }

    /// <summary>Card detection reliability (contour quality, perspective consistency). 0 = poor, 1 = confident.</summary>
    public required double DetectionReliability { get; init; }

    /// <summary>Agreement between CV and ML defect detections. 0 = total disagreement, 1 = full agreement.</summary>
    public required double CvMlAgreement { get; init; }

    /// <summary>Border detection consistency across multiple edge maps. 0 = inconsistent, 1 = stable.</summary>
    public required double BorderConsistency { get; init; }

    /// <summary>Weighted overall confidence. Below 0.5 indicates "low confidence" warning.</summary>
    public required double Overall { get; init; }

    /// <summary>Human-readable summary of confidence assessment.</summary>
    public required string Summary { get; init; }

    /// <summary>True when overall confidence is below the acceptable threshold.</summary>
    public bool IsLowConfidence => Overall < 0.5;
}
