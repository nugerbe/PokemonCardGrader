using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PokemonCardGrader.Application.Configuration;
using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Infrastructure.ML;

/// <summary>
/// Phase 17: Tracks grading accuracy, defect detection precision/recall,
/// and confidence alignment. Computes evaluation metrics from ground-truth data
/// and persists metric snapshots for trend analysis.
/// </summary>
public sealed class EvaluationService(
    IOptions<CardAnalysisOptions> options,
    ILogger<EvaluationService> logger)
{
    private readonly CardAnalysisOptions _opts = options.Value;

    public sealed record EvaluationSample(
        double PredictedGrade,
        double ActualGrade,
        string GradingCompany,
        List<string> PredictedDefectTypes,
        List<string> ActualDefectTypes,
        double Confidence,
        bool WasCorrect);

    /// <summary>
    /// Computes evaluation metrics from a collection of evaluation samples.
    /// </summary>
    public EvaluationMetrics Evaluate(IReadOnlyList<EvaluationSample> samples)
    {
        if (samples.Count == 0)
        {
            return CreateEmptyMetrics();
        }

        // Grade accuracy
        var errors = samples.Select(s => Math.Abs(s.PredictedGrade - s.ActualGrade)).ToList();
        var mae = errors.Average();
        var rmse = Math.Sqrt(errors.Average(e => e * e));
        var halfPoint = samples.Count(s =>
            Math.Abs(s.PredictedGrade - s.ActualGrade) <= 0.5) / (double)samples.Count;
        var onePoint = samples.Count(s =>
            Math.Abs(s.PredictedGrade - s.ActualGrade) <= 1.0) / (double)samples.Count;

        // Defect detection metrics
        var (precision, recall) = ComputeDefectPrecisionRecall(samples);

        // Confidence alignment
        var confidenceCorrelation = ComputeConfidenceCorrelation(samples);
        var ece = ComputeExpectedCalibrationError(samples);

        // Per-company breakdown
        var perCompany = samples
            .GroupBy(s => s.GradingCompany)
            .ToDictionary(
                g => g.Key,
                g => Math.Round(g.Average(s => Math.Abs(s.PredictedGrade - s.ActualGrade)), 4));

        var metrics = new EvaluationMetrics
        {
            ComputedAt = DateTimeOffset.UtcNow,
            TotalSamples = samples.Count,
            GradeMae = Math.Round(mae, 4),
            GradeRmse = Math.Round(rmse, 4),
            GradeAccuracyHalfPoint = Math.Round(halfPoint, 4),
            GradeAccuracyOnePoint = Math.Round(onePoint, 4),
            DefectPrecision = Math.Round(precision, 4),
            DefectRecall = Math.Round(recall, 4),
            ConfidenceCorrelation = Math.Round(confidenceCorrelation, 4),
            ExpectedCalibrationError = Math.Round(ece, 4),
            PerCompanyMae = perCompany
        };

        logger.LogInformation(
            "Evaluation: MAE={Mae:F3} RMSE={Rmse:F3} Acc@0.5={Half:P1} " +
            "DefP={Prec:F2} DefR={Rec:F2} ECE={Ece:F3} samples={N}",
            metrics.GradeMae, metrics.GradeRmse, metrics.GradeAccuracyHalfPoint,
            precision, recall, ece, samples.Count);

        PersistMetrics(metrics);
        return metrics;
    }

    /// <summary>
    /// Loads the latest persisted evaluation metrics, or null if none exist.
    /// </summary>
    public EvaluationMetrics? LoadLatestMetrics()
    {
        var dir = _opts.EvaluationDataPath;
        if (!Directory.Exists(dir)) return null;

        var latestFile = Directory.GetFiles(dir, "eval-*.json")
            .OrderByDescending(f => f)
            .FirstOrDefault();

        if (latestFile is null) return null;

        try
        {
            var json = File.ReadAllText(latestFile);
            return JsonSerializer.Deserialize<EvaluationMetrics>(json);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load evaluation metrics from {Path}.", latestFile);
            return null;
        }
    }

    private static (double Precision, double Recall) ComputeDefectPrecisionRecall(
        IReadOnlyList<EvaluationSample> samples)
    {
        var totalTruePositives = 0;
        var totalPredicted = 0;
        var totalActual = 0;

        foreach (var sample in samples)
        {
            var predicted = sample.PredictedDefectTypes;
            var actual = sample.ActualDefectTypes;

            totalPredicted += predicted.Count;
            totalActual += actual.Count;

            // Count matches (same type present)
            var actualSet = actual.ToHashSet(StringComparer.OrdinalIgnoreCase);
            totalTruePositives += predicted.Count(p => actualSet.Contains(p));
        }

        var precision = totalPredicted > 0 ? (double)totalTruePositives / totalPredicted : 1.0;
        var recall = totalActual > 0 ? (double)totalTruePositives / totalActual : 1.0;

        return (precision, recall);
    }

    private static double ComputeConfidenceCorrelation(IReadOnlyList<EvaluationSample> samples)
    {
        if (samples.Count < 3) return 0;

        var confidences = samples.Select(s => s.Confidence).ToArray();
        var correctness = samples.Select(s => s.WasCorrect ? 1.0 : 0.0).ToArray();

        var meanConf = confidences.Average();
        var meanCorr = correctness.Average();

        var numerator = 0.0;
        var denomConfSq = 0.0;
        var denomCorrSq = 0.0;

        for (var i = 0; i < samples.Count; i++)
        {
            var dc = confidences[i] - meanConf;
            var dr = correctness[i] - meanCorr;
            numerator += dc * dr;
            denomConfSq += dc * dc;
            denomCorrSq += dr * dr;
        }

        var denom = Math.Sqrt(denomConfSq * denomCorrSq);
        return denom > 0 ? numerator / denom : 0;
    }

    private static double ComputeExpectedCalibrationError(IReadOnlyList<EvaluationSample> samples)
    {
        // Bin-based ECE with 10 bins
        const int bins = 10;
        var binCorrect = new double[bins];
        var binConfidence = new double[bins];
        var binCount = new int[bins];

        foreach (var sample in samples)
        {
            var bin = Math.Min((int)(sample.Confidence * bins), bins - 1);
            binConfidence[bin] += sample.Confidence;
            binCorrect[bin] += sample.WasCorrect ? 1.0 : 0.0;
            binCount[bin]++;
        }

        var ece = 0.0;
        for (var b = 0; b < bins; b++)
        {
            if (binCount[b] == 0) continue;

            var avgConf = binConfidence[b] / binCount[b];
            var avgAcc = binCorrect[b] / binCount[b];
            ece += (double)binCount[b] / samples.Count * Math.Abs(avgAcc - avgConf);
        }

        return ece;
    }

    private void PersistMetrics(EvaluationMetrics metrics)
    {
        var dir = _opts.EvaluationDataPath;
        if (!Directory.Exists(dir))
        {
            try { Directory.CreateDirectory(dir); }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to create evaluation directory at {Path}.", dir);
                return;
            }
        }

        var fileName = $"eval-{metrics.ComputedAt:yyyyMMdd-HHmmss}.json";
        var path = Path.Combine(dir, fileName);

        try
        {
            var json = JsonSerializer.Serialize(metrics, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist evaluation metrics to {Path}.", path);
        }
    }

    private static EvaluationMetrics CreateEmptyMetrics() => new()
    {
        ComputedAt = DateTimeOffset.UtcNow,
        TotalSamples = 0,
        GradeMae = 0,
        GradeRmse = 0,
        GradeAccuracyHalfPoint = 0,
        GradeAccuracyOnePoint = 0,
        DefectPrecision = 0,
        DefectRecall = 0,
        ConfidenceCorrelation = 0,
        ExpectedCalibrationError = 0,
        PerCompanyMae = new Dictionary<string, double>()
    };
}
