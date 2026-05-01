using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCvSharp;
using PokemonCardGrader.Application.Configuration;
using PokemonCardGrader.Application.Interfaces;
using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Infrastructure.ML;

/// <summary>
/// Preprocesses normalized card images and runs ONNX model inference for
/// defect detection and surface grading. Handles image preprocessing (resize,
/// normalize, BGR→RGB, HWC→CHW conversion) and output parsing for common
/// detection model output formats.
///
/// ML results augment (not replace) the CV-based analysis from <see cref="ImageAnalysis.ConditionAnalyzer"/>.
/// </summary>
public sealed class OnnxInferenceService(
    IMLModelRegistry modelRegistry,
    IOptions<CardAnalysisOptions> options,
    ILogger<OnnxInferenceService> logger)
{
    private readonly CardAnalysisOptions _opts = options.Value;

    // ImageNet normalization constants (used by most pretrained vision models)
    private static readonly float[] MeanRgb = [0.485f, 0.456f, 0.406f];
    private static readonly float[] StdRgb = [0.229f, 0.224f, 0.225f];

    /// <summary>
    /// Defect class labels matching common card defect detection model vocabularies.
    /// Index corresponds to class ID in model output.
    /// </summary>
    private static readonly string[] DefectClassLabels =
        ["scratch", "dent", "crease", "stain", "whitening", "indent", "print_line"];

    /// <summary>Result of ML inference on a card image.</summary>
    public sealed record InferenceResult(
        List<DetectedDefect> Defects,
        double? SurfaceScore,
        bool DefectModelAvailable,
        bool SurfaceModelAvailable);

    /// <summary>
    /// Runs all available ML models against the normalized card image.
    /// Returns defect detections and surface grade predictions.
    /// Gracefully handles missing models and inference failures.
    /// </summary>
    public async Task<InferenceResult> RunAsync(Mat normalizedImage, CancellationToken ct = default)
    {
        var defects = new List<DetectedDefect>();
        double? surfaceScore = null;
        var defectModelAvailable = false;
        var surfaceModelAvailable = false;

        // Preprocess once — shared across all models
        var inputWidth = _opts.MlInputWidth;
        var inputHeight = _opts.MlInputHeight;
        var tensor = PreprocessImage(normalizedImage, inputWidth, inputHeight);

        // ── Defect detection ──
        var defectModel = modelRegistry.GetByPurpose(MLModelPurpose.DefectClassification);
        if (defectModel is { IsReady: true })
        {
            defectModelAvailable = true;
            try
            {
                var inputName = ResolveInputName(defectModel);
                var input = new Dictionary<string, float[]> { [inputName] = tensor };
                var outputs = await defectModel.InferAsync(input, ct);
                defects = ParseDefectDetections(outputs);
                logger.LogDebug(
                    "ONNX defect detection ({ModelId}) found {Count} defects above threshold {Threshold:F2}.",
                    defectModel.ModelId, defects.Count, _opts.MlDefectConfidenceThreshold);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "ONNX defect detection failed for model {ModelId}.", defectModel.ModelId);
            }
        }
        else
        {
            logger.LogDebug("No ready DefectClassification model registered.");
        }

        // ── Surface grading ──
        var surfaceModel = modelRegistry.GetByPurpose(MLModelPurpose.SurfaceGrading);
        if (surfaceModel is { IsReady: true })
        {
            surfaceModelAvailable = true;
            try
            {
                var inputName = ResolveInputName(surfaceModel);
                var input = new Dictionary<string, float[]> { [inputName] = tensor };
                var outputs = await surfaceModel.InferAsync(input, ct);
                surfaceScore = ParseSurfaceGrade(outputs);
                logger.LogDebug(
                    "ONNX surface grading ({ModelId}): {Score}",
                    surfaceModel.ModelId, surfaceScore?.ToString("F2") ?? "null");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "ONNX surface grading failed for model {ModelId}.", surfaceModel.ModelId);
            }
        }
        else
        {
            logger.LogDebug("No ready SurfaceGrading model registered.");
        }

        return new InferenceResult(defects, surfaceScore, defectModelAvailable, surfaceModelAvailable);
    }

    // ── Image Preprocessing ──

    /// <summary>
    /// Preprocesses a BGR OpenCV Mat into a float tensor in NCHW format:
    /// resize → BGR→RGB → float [0,1] → ImageNet normalize → CHW layout.
    /// </summary>
    private static float[] PreprocessImage(Mat image, int targetWidth, int targetHeight)
    {
        using var resized = new Mat();
        Cv2.Resize(image, resized, new Size(targetWidth, targetHeight),
            interpolation: InterpolationFlags.Linear);

        using var rgb = new Mat();
        Cv2.CvtColor(resized, rgb, ColorConversionCodes.BGR2RGB);

        // Convert to float32 and normalize to [0, 1]
        using var floatMat = new Mat();
        rgb.ConvertTo(floatMat, MatType.CV_32FC3, 1.0 / 255.0);

        // Split into individual channels (R, G, B)
        Cv2.Split(floatMat, out var channels);
        try
        {
            var totalPixels = targetWidth * targetHeight;
            var tensor = new float[3 * totalPixels];

            for (var c = 0; c < 3; c++)
            {
                // Apply ImageNet normalization: (pixel - mean) / std
                using var subtracted = new Mat();
                Cv2.Subtract(channels[c], new Scalar(MeanRgb[c]), subtracted);
                using var normalized = new Mat();
                Cv2.Divide(subtracted, new Scalar(StdRgb[c]), normalized);

                // Copy continuous float data into the tensor at the channel offset
                var channelData = new float[totalPixels];
                Marshal.Copy(normalized.Data, channelData, 0, totalPixels);
                Array.Copy(channelData, 0, tensor, c * totalPixels, totalPixels);
            }

            return tensor;
        }
        finally
        {
            foreach (var ch in channels) ch.Dispose();
        }
    }

    // ── Output Parsing ──

    /// <summary>
    /// Parses defect detection model outputs. Supports multiple common formats:
    /// 1) Separate boxes/scores/labels tensors
    /// 2) Combined detections tensor [N×6]: [x1,y1,x2,y2,score,class]
    /// 3) Single flat output (YOLO-style post-processed)
    /// </summary>
    private List<DetectedDefect> ParseDefectDetections(Dictionary<string, float[]> outputs)
    {
        // Format 1: separate tensors (TensorFlow-style)
        if (TryGetOutput(outputs, ["boxes", "detection_boxes", "pred_boxes"], out var boxes) &&
            TryGetOutput(outputs, ["scores", "detection_scores", "pred_scores"], out var scores))
        {
            TryGetOutput(outputs, ["classes", "detection_classes", "labels", "pred_labels"], out var classes);
            return ParseSeparateTensors(boxes, scores, classes);
        }

        // Format 2: combined detections tensor
        if (TryGetOutput(outputs, ["detections", "output0", "output"], out var combined) &&
            combined.Length >= 6)
        {
            return ParseCombinedTensor(combined);
        }

        // Format 3: single output — try to interpret as packed detections
        if (outputs.Count == 1)
        {
            var singleOutput = outputs.Values.First();
            if (singleOutput.Length >= 6)
                return ParseCombinedTensor(singleOutput);
        }

        logger.LogDebug(
            "No recognized defect detection output format. Keys: [{Keys}], sizes: [{Sizes}]",
            string.Join(", ", outputs.Keys),
            string.Join(", ", outputs.Values.Select(v => v.Length)));
        return [];
    }

    /// <summary>
    /// Parses separate boxes [N*4], scores [N], and optional classes [N] tensors.
    /// Box format: [x1, y1, x2, y2] normalized to [0, 1].
    /// </summary>
    private List<DetectedDefect> ParseSeparateTensors(float[] boxes, float[] scores, float[]? classes)
    {
        var numDetections = scores.Length;
        if (boxes.Length < numDetections * 4)
        {
            logger.LogDebug(
                "Box tensor length {BoxLen} insufficient for {N} detections (need {Need}).",
                boxes.Length, numDetections, numDetections * 4);
            return [];
        }

        var defects = new List<DetectedDefect>();

        for (var i = 0; i < numDetections && defects.Count < _opts.MlMaxDefects; i++)
        {
            var confidence = scores[i];
            if (confidence < _opts.MlDefectConfidenceThreshold)
                continue;

            var x1 = Math.Clamp(boxes[i * 4], 0f, 1f);
            var y1 = Math.Clamp(boxes[i * 4 + 1], 0f, 1f);
            var x2 = Math.Clamp(boxes[i * 4 + 2], 0f, 1f);
            var y2 = Math.Clamp(boxes[i * 4 + 3], 0f, 1f);

            var classIdx = classes is not null && i < classes.Length ? (int)classes[i] : 0;
            var defectType = classIdx >= 0 && classIdx < DefectClassLabels.Length
                ? DefectClassLabels[classIdx]
                : $"unknown_{classIdx}";

            var width = Math.Abs(x2 - x1);
            var height = Math.Abs(y2 - y1);
            if (width < 0.001 || height < 0.001)
                continue;

            // Severity derived from detection area and confidence
            var area = width * height;
            var severity = Math.Clamp(area * 10.0 + confidence * 0.5, 0.0, 1.0);

            defects.Add(new DetectedDefect
            {
                Type = defectType,
                Severity = Math.Round(severity, 3),
                X = Math.Round(Math.Min(x1, x2), 4),
                Y = Math.Round(Math.Min(y1, y2), 4),
                Width = Math.Round(width, 4),
                Height = Math.Round(height, 4),
                Confidence = Math.Round(confidence, 3)
            });
        }

        return defects.OrderByDescending(d => d.Confidence).ToList();
    }

    /// <summary>
    /// Parses a combined detections tensor with stride 6 or 7:
    /// [x1, y1, x2, y2, score, class_id] per detection.
    /// If stride is 7, first element is batch index (ignored).
    /// </summary>
    private List<DetectedDefect> ParseCombinedTensor(float[] data)
    {
        // Determine stride: 6 (x1,y1,x2,y2,score,class) or 7 (batch,class,score,x1,y1,x2,y2)
        var stride = data.Length % 7 == 0 && data.Length % 6 != 0 ? 7 : 6;
        var numDetections = data.Length / stride;
        var defects = new List<DetectedDefect>();

        for (var i = 0; i < numDetections && defects.Count < _opts.MlMaxDefects; i++)
        {
            var offset = i * stride;

            float x1, y1, x2, y2, score;
            int classIdx;

            if (stride == 7)
            {
                // SSD-style: [batch_id, class_id, score, x1, y1, x2, y2]
                classIdx = (int)data[offset + 1];
                score = data[offset + 2];
                x1 = data[offset + 3];
                y1 = data[offset + 4];
                x2 = data[offset + 5];
                y2 = data[offset + 6];
            }
            else
            {
                // Standard: [x1, y1, x2, y2, score, class_id]
                x1 = data[offset];
                y1 = data[offset + 1];
                x2 = data[offset + 2];
                y2 = data[offset + 3];
                score = data[offset + 4];
                classIdx = (int)data[offset + 5];
            }

            if (score < _opts.MlDefectConfidenceThreshold)
                continue;

            x1 = Math.Clamp(x1, 0f, 1f);
            y1 = Math.Clamp(y1, 0f, 1f);
            x2 = Math.Clamp(x2, 0f, 1f);
            y2 = Math.Clamp(y2, 0f, 1f);

            var width = Math.Abs(x2 - x1);
            var height = Math.Abs(y2 - y1);
            if (width < 0.001 || height < 0.001)
                continue;

            var defectType = classIdx >= 0 && classIdx < DefectClassLabels.Length
                ? DefectClassLabels[classIdx]
                : $"unknown_{classIdx}";

            var area = width * height;
            var severity = Math.Clamp(area * 10.0 + score * 0.5, 0.0, 1.0);

            defects.Add(new DetectedDefect
            {
                Type = defectType,
                Severity = Math.Round(severity, 3),
                X = Math.Round(Math.Min(x1, x2), 4),
                Y = Math.Round(Math.Min(y1, y2), 4),
                Width = Math.Round(width, 4),
                Height = Math.Round(height, 4),
                Confidence = Math.Round(score, 3)
            });
        }

        return defects.OrderByDescending(d => d.Confidence).ToList();
    }

    /// <summary>
    /// Parses surface grading model output. Supports:
    /// 1) Single scalar grade (1-10)
    /// 2) Probability distribution over grade buckets → expected value
    /// </summary>
    private double? ParseSurfaceGrade(Dictionary<string, float[]> outputs)
    {
        // Direct grade output (single value)
        if (TryGetOutput(outputs, ["grade", "score", "surface_score"], out var gradeArr) &&
            gradeArr.Length == 1)
        {
            return Math.Clamp(Math.Round(gradeArr[0] * 2) / 2, 1.0, 10.0);
        }

        // Probability distribution over grade buckets
        if (TryGetOutput(outputs, ["probabilities", "logits", "output", "grade_dist"], out var probs) &&
            probs.Length > 1)
        {
            var softmaxed = Softmax(probs);
            var expectedGrade = 0.0;
            for (var i = 0; i < softmaxed.Length; i++)
            {
                // Buckets map linearly from 1.0 to 10.0
                var bucketGrade = 1.0 + (9.0 * i / Math.Max(softmaxed.Length - 1, 1));
                expectedGrade += softmaxed[i] * bucketGrade;
            }
            return Math.Clamp(Math.Round(expectedGrade * 2) / 2, 1.0, 10.0);
        }

        // Fallback: single output tensor, take first value
        if (outputs.Count > 0)
        {
            var firstOutput = outputs.Values.First();
            if (firstOutput.Length > 0)
                return Math.Clamp(Math.Round(firstOutput[0] * 2) / 2, 1.0, 10.0);
        }

        logger.LogDebug(
            "Could not parse surface grade output. Keys: [{Keys}]",
            string.Join(", ", outputs.Keys));
        return null;
    }

    // ── Helpers ──

    /// <summary>
    /// Resolves the primary input tensor name for a model.
    /// Uses the model's own InputNames if available, otherwise falls back to "input".
    /// </summary>
    private static string ResolveInputName(IMLModel model)
    {
        return model.InputNames.Count > 0 ? model.InputNames[0] : "input";
    }

    /// <summary>
    /// Tries to find an output tensor by any of the candidate names (case-insensitive).
    /// </summary>
    private static bool TryGetOutput(
        Dictionary<string, float[]> outputs, string[] candidateNames, out float[] result)
    {
        foreach (var name in candidateNames)
        {
            // Exact match first
            if (outputs.TryGetValue(name, out var exact))
            {
                result = exact;
                return true;
            }

            // Case-insensitive fallback
            var match = outputs.FirstOrDefault(kv =>
                kv.Key.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (match.Value is not null)
            {
                result = match.Value;
                return true;
            }
        }

        result = [];
        return false;
    }

    /// <summary>
    /// Computes softmax over a float array (numerically stable with max subtraction).
    /// </summary>
    private static double[] Softmax(float[] logits)
    {
        var max = logits.Max();
        var exps = logits.Select(x => Math.Exp(x - max)).ToArray();
        var sum = exps.Sum();
        return sum > 0 ? exps.Select(e => e / sum).ToArray() : exps;
    }
}
