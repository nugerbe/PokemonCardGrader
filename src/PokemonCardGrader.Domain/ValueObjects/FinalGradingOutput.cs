namespace PokemonCardGrader.Domain.ValueObjects;

/// <summary>
/// The final structured output of the grading pipeline (Phase 24).
/// Contains everything needed for the API response and UI display.
/// </summary>
public sealed record FinalGradingOutput
{
    /// <summary>Submission identifier.</summary>
    public required Guid SubmissionId { get; init; }

    /// <summary>When analysis was completed.</summary>
    public required DateTimeOffset CompletedAt { get; init; }

    /// <summary>Grade estimates for each grading company.</summary>
    public required List<GradeResult> Grades { get; init; }

    /// <summary>Overall pipeline confidence (0-1).</summary>
    public required double Confidence { get; init; }

    /// <summary>Detailed confidence breakdown.</summary>
    public required ConfidenceBreakdown? ConfidenceDetail { get; init; }

    /// <summary>Centering measurements.</summary>
    public required CenteringMeasurement? Centering { get; init; }

    /// <summary>Condition sub-grades (1-10 scale).</summary>
    public required ConditionScores Scores { get; init; }

    /// <summary>All detected defects with severity, location, and confidence.</summary>
    public required List<DefectReport> Defects { get; init; }

    /// <summary>Human-readable grading explanation.</summary>
    public required GradingReport Report { get; init; }

    /// <summary>Image quality assessment.</summary>
    public required ImageQualityAssessment? QualityAssessment { get; init; }

    /// <summary>Failure/validation result.</summary>
    public required FailureDetectionResult? FailureDetection { get; init; }

    /// <summary>Analysis method identifier (e.g., "OpenCV-v4+ML").</summary>
    public required string AnalysisMethod { get; init; }

    /// <summary>Overlay data for UI rendering.</summary>
    public required AnalysisOverlay? Overlay { get; init; }
}

/// <summary>
/// A defect with full context for the final output.
/// </summary>
public sealed record DefectReport
{
    public required string Type { get; init; }
    public required double Severity { get; init; }
    public required string SeverityLabel { get; init; }
    public required double X { get; init; }
    public required double Y { get; init; }
    public required double Width { get; init; }
    public required double Height { get; init; }
    public required double Confidence { get; init; }
    public required string Region { get; init; }
    public required string Description { get; init; }

    public static DefectReport FromDefect(DetectedDefect defect, string region) => new()
    {
        Type = defect.Type,
        Severity = defect.Severity,
        SeverityLabel = defect.Severity switch
        {
            < 0.2 => "Minor",
            < 0.5 => "Moderate",
            < 0.8 => "Significant",
            _ => "Severe"
        },
        X = defect.X,
        Y = defect.Y,
        Width = defect.Width,
        Height = defect.Height,
        Confidence = defect.Confidence,
        Region = region,
        Description = $"{defect.Type} ({defect.Severity:F2} severity) in {region}"
    };
}
