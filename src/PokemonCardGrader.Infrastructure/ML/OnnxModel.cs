using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using PokemonCardGrader.Application.Interfaces;

namespace PokemonCardGrader.Infrastructure.ML;

/// <summary>
/// ONNX Runtime-based ML model implementation.
/// Loads an ONNX model file and runs inference via Microsoft.ML.OnnxRuntime.
/// Thread-safe: the InferenceSession is created once and reused.
/// </summary>
public sealed class OnnxModel : IMLModel
{
    private readonly ILogger<OnnxModel> _logger;
    private readonly InferenceSession? _session;
    private readonly Dictionary<string, int[]> _inputShapes;
    private bool _disposed;

    public string ModelId { get; }
    public MLModelPurpose Purpose { get; }
    public bool IsReady => _session is not null;
    public IReadOnlyList<string> InputNames => [.. _inputShapes.Keys];
    public IReadOnlyList<string> OutputNames { get; private set; } = [];

    public OnnxModel(string modelId, MLModelPurpose purpose, string modelPath, ILogger<OnnxModel> logger)
    {
        ModelId = modelId;
        Purpose = purpose;
        _logger = logger;
        _inputShapes = new Dictionary<string, int[]>();

        if (!File.Exists(modelPath))
        {
            _logger.LogWarning(
                "ONNX model file not found at {Path} for {ModelId}. Model will be unavailable.",
                modelPath, modelId);
            return;
        }

        try
        {
            var sessionOptions = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
                InterOpNumThreads = 1,
                IntraOpNumThreads = Environment.ProcessorCount > 1 ? 2 : 1
            };

            _session = new InferenceSession(modelPath, sessionOptions);

            // Cache input metadata for shape validation
            foreach (var input in _session.InputMetadata)
            {
                _inputShapes[input.Key] = input.Value.Dimensions;
                _logger.LogDebug(
                    "ONNX input '{Name}': type={Type}, shape=[{Shape}]",
                    input.Key, input.Value.ElementType,
                    string.Join(",", input.Value.Dimensions));
            }

            OutputNames = [.. _session.OutputMetadata.Keys];
            foreach (var output in _session.OutputMetadata)
            {
                _logger.LogDebug(
                    "ONNX output '{Name}': type={Type}, shape=[{Shape}]",
                    output.Key, output.Value.ElementType,
                    string.Join(",", output.Value.Dimensions));
            }

            _logger.LogInformation(
                "ONNX model loaded: {ModelId} ({Purpose}) from {Path} with {InputCount} inputs, {OutputCount} outputs.",
                modelId, purpose, modelPath,
                _session.InputMetadata.Count, _session.OutputMetadata.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load ONNX model {ModelId} from {Path}.", modelId, modelPath);
            _session = null;
        }
    }

    public Task<Dictionary<string, float[]>> InferAsync(
        Dictionary<string, float[]> input, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_session is null)
            throw new InvalidOperationException($"Model {ModelId} is not loaded.");

        ct.ThrowIfCancellationRequested();

        var namedValues = new List<NamedOnnxValue>();

        foreach (var (name, data) in input)
        {
            if (!_session.InputMetadata.TryGetValue(name, out var meta))
            {
                _logger.LogWarning(
                    "Input tensor '{Name}' not found in model {ModelId} metadata. Skipping.",
                    name, ModelId);
                continue;
            }

            // Build the shape: replace -1 (dynamic) dimensions with inferred sizes
            var shape = ResolveShape(meta.Dimensions, data.Length);
            var tensor = new DenseTensor<float>(data, shape);
            namedValues.Add(NamedOnnxValue.CreateFromTensor(name, tensor));
        }

        if (namedValues.Count == 0)
            return Task.FromResult(new Dictionary<string, float[]>());

        using var results = _session.Run(namedValues);

        var output = new Dictionary<string, float[]>();
        foreach (var result in results)
        {
            if (result.Value is DenseTensor<float> tensor)
            {
                output[result.Name] = tensor.ToArray();
            }
            else if (result.Value is IEnumerable<float> enumerable)
            {
                output[result.Name] = enumerable.ToArray();
            }
            else
            {
                _logger.LogDebug(
                    "Output '{Name}' type {Type} not directly convertible to float[]. Attempting cast.",
                    result.Name, result.Value?.GetType().Name ?? "null");

                try
                {
                    var asSpan = result.AsEnumerable<float>().ToArray();
                    output[result.Name] = asSpan;
                }
                catch
                {
                    output[result.Name] = [];
                }
            }
        }

        _logger.LogDebug(
            "ONNX inference on {ModelId}: {InputCount} inputs → {OutputCount} outputs.",
            ModelId, namedValues.Count, output.Count);

        return Task.FromResult(output);
    }

    /// <summary>
    /// Resolves dynamic (-1) dimensions in the model shape using the actual data length.
    /// For a shape like [-1, 3, 224, 224], computes batch size from data length.
    /// </summary>
    private static int[] ResolveShape(int[] modelDimensions, int dataLength)
    {
        var shape = (int[])modelDimensions.Clone();
        var dynamicIndex = Array.IndexOf(shape, -1);

        if (dynamicIndex >= 0)
        {
            var knownProduct = 1;
            for (var i = 0; i < shape.Length; i++)
            {
                if (i != dynamicIndex && shape[i] > 0)
                    knownProduct *= shape[i];
            }

            shape[dynamicIndex] = knownProduct > 0 ? dataLength / knownProduct : 1;
        }

        return shape;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session?.Dispose();
    }
}
