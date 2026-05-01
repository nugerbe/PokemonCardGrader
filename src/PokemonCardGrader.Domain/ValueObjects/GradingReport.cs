namespace PokemonCardGrader.Domain.ValueObjects;

/// <summary>
/// Structured, human-readable explainability output for a grading result.
/// Provides justifications for each sub-grade and the overall assessment.
/// </summary>
public sealed record GradingReport
{
    /// <summary>One-sentence overall summary (e.g., "This card grades PSA 8 (NM-MT) with strong surface but off-center printing.").</summary>
    public required string OverallSummary { get; init; }

    /// <summary>Centering explanation with L/R and T/B percentages and impact on grade.</summary>
    public required string CenteringSummary { get; init; }

    /// <summary>Surface condition explanation with sharpness and defect details.</summary>
    public required string SurfaceSummary { get; init; }

    /// <summary>Corner condition explanation with whitening and damage details.</summary>
    public required string CornersSummary { get; init; }

    /// <summary>Edge condition explanation with wear and whitening details.</summary>
    public required string EdgesSummary { get; init; }

    /// <summary>Individual notes for each detected defect.</summary>
    public required List<string> DefectNotes { get; init; }

    /// <summary>Confidence assessment note (e.g., "High confidence — clear image, CV/ML agreement").</summary>
    public required string ConfidenceNote { get; init; }

    /// <summary>Ordered list of reasons why the final grade was assigned.</summary>
    public required List<string> GradeJustifications { get; init; }

    /// <summary>ML model contributions, if any models were used.</summary>
    public required string MlContribution { get; init; }
}
