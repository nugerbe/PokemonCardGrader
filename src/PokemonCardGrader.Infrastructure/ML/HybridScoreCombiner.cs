using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PokemonCardGrader.Application.Configuration;
using PokemonCardGrader.Application.Interfaces;
using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Infrastructure.ML;

/// <summary>
/// Phase 15: Combines CV-derived scores with ML model predictions using a weighted
/// ensemble strategy. Supports object detection, classification, and regression models
/// through the IMLModel/IMLModelRegistry abstraction.
/// </summary>
public sealed class HybridScoreCombiner(
    IMLModelRegistry modelRegistry,
    IOptions<CardAnalysisOptions> options,
    ILogger<HybridScoreCombiner> logger)
{
    private readonly CardAnalysisOptions _opts = options.Value;

    public sealed record HybridResult(
        double CornersScore,
        double EdgesScore,
        double SurfaceScore,
        List<DetectedDefect> CombinedDefects,
        double MlConfidence,
        bool MlAvailable);

    /// <summary>
    /// Combines CV condition scores with ML predictions using configurable weights.
    /// Falls back to CV-only when ML models are unavailable or low-confidence.
    /// </summary>
    public async Task<HybridResult> CombineAsync(
        double cvCorners, double cvEdges, double cvSurface,
        List<DetectedDefect> cvDefects,
        CardFeatures features,
        OnnxInferenceService.InferenceResult mlResult,
        CancellationToken ct = default)
    {
        var mlAvailable = mlResult.SurfaceModelAvailable || mlResult.DefectModelAvailable;
        var mlConfidence = 0.0;

        // Try feature-based ML prediction if a grade prediction model exists
        var gradeModel = modelRegistry.GetByPurpose(MLModelPurpose.GradePrediction);
        double? mlCorners = null, mlEdges = null, mlSurface = mlResult.SurfaceScore;
        List<DetectedDefect>? mlDefects = mlResult.Defects.Count > 0 ? mlResult.Defects : null;

        if (gradeModel is { IsReady: true })
        {
            try
            {
                var featureArray = features.ToFlatArray();
                var input = new Dictionary<string, float[]>
                {
                    ["features"] = featureArray.Select(d => (float)d).ToArray()
                };

                var output = await gradeModel.InferAsync(input, ct);

                if (output.TryGetValue("scores", out var scores) && scores.Length >= 3)
                {
                    mlCorners = Math.Clamp(scores[0] * 10.0, 1.0, 10.0);
                    mlEdges = Math.Clamp(scores[1] * 10.0, 1.0, 10.0);
                    mlSurface = Math.Clamp(scores[2] * 10.0, 1.0, 10.0);
                    mlAvailable = true;
                }

                if (output.TryGetValue("confidence", out var conf) && conf.Length > 0)
                {
                    mlConfidence = conf[0];
                }
                else
                {
                    mlConfidence = 0.7; // Default when model doesn't output confidence
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Grade prediction model inference failed; falling back to CV-only.");
            }
        }

        // Combine scores
        if (!mlAvailable || mlConfidence < _opts.HybridMinMlConfidence)
        {
            logger.LogDebug("Hybrid: ML unavailable or low confidence ({Confidence:F2}), using CV-only.",
                mlConfidence);

            return new HybridResult(
                CornersScore: cvCorners,
                EdgesScore: cvEdges,
                SurfaceScore: cvSurface,
                CombinedDefects: cvDefects,
                MlConfidence: 0,
                MlAvailable: false);
        }

        var cvWeight = _opts.HybridCvWeight;
        var mlWeight = _opts.HybridMlWeight;

        // Scale ML weight by confidence
        mlWeight *= mlConfidence;
        var totalWeight = cvWeight + mlWeight;
        cvWeight /= totalWeight;
        mlWeight /= totalWeight;

        var combinedCorners = Math.Round(cvCorners * cvWeight + (mlCorners ?? cvCorners) * mlWeight, 1);
        var combinedEdges = Math.Round(cvEdges * cvWeight + (mlEdges ?? cvEdges) * mlWeight, 1);
        var combinedSurface = Math.Round(cvSurface * cvWeight + (mlSurface ?? cvSurface) * mlWeight, 1);

        // Combine defects: merge CV and ML, dedup by proximity
        var combinedDefects = MergeDefects(cvDefects, mlDefects ?? []);

        logger.LogInformation(
            "Hybrid: CV({CvW:F2}) + ML({MlW:F2}) => corners={C:F1} edges={E:F1} surface={S:F1} defects={D}",
            cvWeight, mlWeight, combinedCorners, combinedEdges, combinedSurface, combinedDefects.Count);

        return new HybridResult(
            CornersScore: Math.Clamp(combinedCorners, 1, 10),
            EdgesScore: Math.Clamp(combinedEdges, 1, 10),
            SurfaceScore: Math.Clamp(combinedSurface, 1, 10),
            CombinedDefects: combinedDefects,
            MlConfidence: mlConfidence,
            MlAvailable: true);
    }

    private static List<DetectedDefect> MergeDefects(
        List<DetectedDefect> cvDefects, List<DetectedDefect> mlDefects)
    {
        if (mlDefects.Count == 0) return cvDefects;
        if (cvDefects.Count == 0) return mlDefects;

        var merged = new List<DetectedDefect>(cvDefects);

        foreach (var mlDefect in mlDefects)
        {
            var isDuplicate = false;
            for (var i = 0; i < merged.Count; i++)
            {
                var existing = merged[i];
                var distance = Math.Sqrt(
                    Math.Pow(mlDefect.X - existing.X, 2) +
                    Math.Pow(mlDefect.Y - existing.Y, 2));

                // Same type and nearby = duplicate, boost confidence
                if (distance < 0.05 && mlDefect.Type == existing.Type)
                {
                    merged[i] = existing with
                    {
                        Confidence = Math.Min(0.98, Math.Max(existing.Confidence, mlDefect.Confidence) + 0.1),
                        Severity = Math.Max(existing.Severity, mlDefect.Severity)
                    };
                    isDuplicate = true;
                    break;
                }
            }

            if (!isDuplicate)
            {
                merged.Add(mlDefect);
            }
        }

        return merged
            .OrderByDescending(d => d.Severity)
            .ToList();
    }
}
