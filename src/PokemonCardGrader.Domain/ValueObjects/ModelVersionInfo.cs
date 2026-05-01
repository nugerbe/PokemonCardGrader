namespace PokemonCardGrader.Domain.ValueObjects;

/// <summary>
/// Metadata for a versioned ML model used in the analysis pipeline.
/// </summary>
public sealed record ModelVersionInfo
{
    public required string ModelId { get; init; }
    public required string Version { get; init; }
    public required string Purpose { get; init; }
    public required DateTimeOffset TrainedAt { get; init; }
    public required DateTimeOffset RegisteredAt { get; init; }
    public required int TrainingSampleCount { get; init; }
    public required string? TrainingDatasetHash { get; init; }

    /// <summary>Performance metrics from evaluation (e.g., "MAE" → 0.3).</summary>
    public required Dictionary<string, double> PerformanceMetrics { get; init; }

    /// <summary>Whether this version is currently active in the pipeline.</summary>
    public required bool IsActive { get; init; }

    /// <summary>Path to the model file on disk.</summary>
    public required string ModelPath { get; init; }

    /// <summary>Optional notes about this version (changes, improvements).</summary>
    public string? Notes { get; init; }
}
