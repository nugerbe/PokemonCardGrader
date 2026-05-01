namespace PokemonCardGrader.Application.Interfaces;

/// <summary>
/// Pluggable ML model interface for card analysis tasks.
/// Implementations can use ONNX Runtime, ML.NET, or any other inference backend.
/// Models are identified by purpose (e.g., "card-detection", "defect-classification", "surface-grading").
/// </summary>
public interface IMLModel : IDisposable
{
    /// <summary>Unique model identifier (e.g., "card-detection-v2", "defect-classifier-v1").</summary>
    string ModelId { get; }

    /// <summary>The task this model handles.</summary>
    MLModelPurpose Purpose { get; }

    /// <summary>Whether the model is loaded and ready for inference.</summary>
    bool IsReady { get; }

    /// <summary>Names of the model's input tensors (used for tensor name resolution).</summary>
    IReadOnlyList<string> InputNames => [];

    /// <summary>Names of the model's output tensors.</summary>
    IReadOnlyList<string> OutputNames => [];

    /// <summary>Runs inference on the given input tensor.</summary>
    /// <param name="input">Named input tensors (name → float array). Shape conventions depend on the model.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Named output tensors (name → float array).</returns>
    Task<Dictionary<string, float[]>> InferAsync(Dictionary<string, float[]> input, CancellationToken ct = default);
}

/// <summary>
/// Describes what a pluggable ML model is used for in the analysis pipeline.
/// </summary>
public enum MLModelPurpose
{
    /// <summary>Refines card boundary detection from initial CV candidates.</summary>
    CardDetection,

    /// <summary>Classifies detected defects (scratch, dent, crease, stain).</summary>
    DefectClassification,

    /// <summary>Predicts surface condition grade from normalized card image features.</summary>
    SurfaceGrading,

    /// <summary>Predicts overall grade from condition scores.</summary>
    GradePrediction
}

/// <summary>
/// Registry for discovering available ML models at runtime.
/// </summary>
public interface IMLModelRegistry
{
    /// <summary>Returns all registered models.</summary>
    IReadOnlyList<IMLModel> GetAll();

    /// <summary>Returns the first model matching the given purpose, or null.</summary>
    IMLModel? GetByPurpose(MLModelPurpose purpose);

    /// <summary>Returns a model by its ID, or null.</summary>
    IMLModel? GetById(string modelId);
}
