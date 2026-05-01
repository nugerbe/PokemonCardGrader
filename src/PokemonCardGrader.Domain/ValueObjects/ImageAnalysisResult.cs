namespace PokemonCardGrader.Domain.ValueObjects;

public sealed record ImageAnalysisResult
{
    public required CenteringMeasurement? DetectedCentering { get; init; }
    public required double? CornersScore { get; init; }
    public required double? EdgesScore { get; init; }
    public required double? SurfaceScore { get; init; }
    public required List<DetectedDefect> DetectedDefects { get; init; }
    public AnalysisOverlay? Overlay { get; init; }
    public required DateTimeOffset AnalyzedAt { get; init; }
    public required string AnalysisMethod { get; init; }

    // ── ML-augmented fields (populated when ONNX models are available) ──

    /// <summary>Defects detected by ML model, separate from CV-detected defects in <see cref="DetectedDefects"/>.</summary>
    public List<DetectedDefect> MlDetectedDefects { get; init; } = [];

    /// <summary>ML-predicted surface condition score (1-10), or null if no surface grading model was available.</summary>
    public double? MlSurfaceScore { get; init; }

    /// <summary>Whether a defect classification model was available during analysis.</summary>
    public bool DefectModelUsed { get; init; }

    /// <summary>Whether a surface grading model was available during analysis.</summary>
    public bool SurfaceModelUsed { get; init; }

    // ── Confidence fields ──

    /// <summary>Input image quality score (0-1). Null if not computed.</summary>
    public double? ImageQualityScore { get; init; }

    /// <summary>Overall pipeline confidence (0-1). Below 0.5 triggers low-confidence warning.</summary>
    public double? OverallConfidence { get; init; }

    /// <summary>Detailed confidence breakdown by component.</summary>
    public ConfidenceBreakdown? ConfidenceDetail { get; init; }

    // ── Phase 10-24 extended fields ──

    /// <summary>Pre-processing image quality assessment (Phase 10).</summary>
    public ImageQualityAssessment? QualityAssessment { get; init; }

    /// <summary>Failure/validation detection result (Phase 21).</summary>
    public FailureDetectionResult? FailureDetection { get; init; }

    /// <summary>Region segmentation data (Phase 12).</summary>
    public CardRegions? Regions { get; init; }

    /// <summary>Extracted feature vector for ML input (Phase 14).</summary>
    public CardFeatures? Features { get; init; }

    /// <summary>Whether hybrid ML/CV combination was used (Phase 15).</summary>
    public bool HybridMlUsed { get; init; }

    /// <summary>ML confidence from hybrid combiner (Phase 15).</summary>
    public double? HybridMlConfidence { get; init; }

    /// <summary>
    /// Perspective-corrected card image as JPEG bytes. Transient (not persisted to the
    /// EF JSON column — EF is configured to ignore this). Used by the worker to write
    /// the rectified image to storage and record the path on <c>CardImage</c>. Null
    /// when analysis failed or no card quad was detected.
    /// </summary>
    public byte[]? NormalizedImageBytes { get; init; }
}
