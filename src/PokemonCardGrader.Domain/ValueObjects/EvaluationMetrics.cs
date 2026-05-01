namespace PokemonCardGrader.Domain.ValueObjects;

/// <summary>
/// Comprehensive evaluation metrics for the grading pipeline.
/// Tracks accuracy, precision, recall, and confidence alignment.
/// </summary>
public sealed record EvaluationMetrics
{
    public required DateTimeOffset ComputedAt { get; init; }
    public required int TotalSamples { get; init; }

    // ── Grade accuracy ──
    /// <summary>Mean absolute error of grade predictions.</summary>
    public required double GradeMae { get; init; }

    /// <summary>Root mean squared error of grade predictions.</summary>
    public required double GradeRmse { get; init; }

    /// <summary>Fraction of predictions within 0.5 of actual grade.</summary>
    public required double GradeAccuracyHalfPoint { get; init; }

    /// <summary>Fraction of predictions within 1.0 of actual grade.</summary>
    public required double GradeAccuracyOnePoint { get; init; }

    // ── Defect detection ──
    /// <summary>Defect detection precision (true positives / all detected).</summary>
    public required double DefectPrecision { get; init; }

    /// <summary>Defect detection recall (true positives / all actual defects).</summary>
    public required double DefectRecall { get; init; }

    /// <summary>Defect detection F1 score.</summary>
    public double DefectF1 => DefectPrecision + DefectRecall > 0
        ? 2 * DefectPrecision * DefectRecall / (DefectPrecision + DefectRecall)
        : 0;

    // ── Confidence alignment ──
    /// <summary>Correlation between confidence and actual correctness (higher = better calibrated).</summary>
    public required double ConfidenceCorrelation { get; init; }

    /// <summary>Expected calibration error (lower = better calibrated).</summary>
    public required double ExpectedCalibrationError { get; init; }

    // ── Per-company breakdown ──
    public required Dictionary<string, double> PerCompanyMae { get; init; }
}
