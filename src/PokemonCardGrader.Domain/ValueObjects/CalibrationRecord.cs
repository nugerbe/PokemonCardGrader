namespace PokemonCardGrader.Domain.ValueObjects;

/// <summary>
/// Records a calibration data point: predicted vs actual grade.
/// Used by the CalibrationService to track prediction accuracy and adjust thresholds.
/// </summary>
public sealed record CalibrationRecord
{
    public required Guid SubmissionId { get; init; }
    public required DateTimeOffset RecordedAt { get; init; }
    public required double PredictedGrade { get; init; }
    public required double ActualGrade { get; init; }
    public required double Error { get; init; }
    public required double SignedError { get; init; }
    public required string GradingCompany { get; init; }
    public required string AnalysisMethod { get; init; }
    public required double Confidence { get; init; }
}

/// <summary>
/// Aggregated calibration metrics computed from historical records.
/// </summary>
public sealed record CalibrationMetrics
{
    /// <summary>Mean absolute error across all calibration records.</summary>
    public required double MeanAbsoluteError { get; init; }

    /// <summary>Mean signed error (positive = over-predicting, negative = under-predicting).</summary>
    public required double MeanSignedError { get; init; }

    /// <summary>Standard deviation of errors.</summary>
    public required double ErrorStdDev { get; init; }

    /// <summary>Fraction of predictions within 0.5 grade points of actual.</summary>
    public required double AccuracyWithinHalfGrade { get; init; }

    /// <summary>Fraction of predictions within 1.0 grade points of actual.</summary>
    public required double AccuracyWithinOneGrade { get; init; }

    /// <summary>Number of calibration records used.</summary>
    public required int SampleCount { get; init; }

    /// <summary>Recommended bias correction to apply to future predictions.</summary>
    public required double BiasCorrection { get; init; }

    /// <summary>Per-score-range accuracy breakdown (key = "8.0-8.5", value = MAE in that range).</summary>
    public required Dictionary<string, double> RangeAccuracy { get; init; }
}
