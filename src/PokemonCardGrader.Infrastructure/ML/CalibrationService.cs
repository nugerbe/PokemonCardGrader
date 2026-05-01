using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PokemonCardGrader.Application.Configuration;
using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Infrastructure.ML;

/// <summary>
/// Phase 16: Compares predictions against ground truth to track error and bias,
/// and computes calibration corrections for future predictions.
/// Persists calibration records to disk as JSON.
/// </summary>
public sealed class CalibrationService(
    IOptions<CardAnalysisOptions> options,
    ILogger<CalibrationService> logger)
{
    private readonly CardAnalysisOptions _opts = options.Value;
    private readonly Lock _lock = new();
    private List<CalibrationRecord>? _records;

    /// <summary>
    /// Records a calibration data point (predicted vs actual grade).
    /// </summary>
    public void RecordCalibration(
        Guid submissionId, double predicted, double actual,
        string company, string method, double confidence)
    {
        var record = new CalibrationRecord
        {
            SubmissionId = submissionId,
            RecordedAt = DateTimeOffset.UtcNow,
            PredictedGrade = predicted,
            ActualGrade = actual,
            Error = Math.Abs(predicted - actual),
            SignedError = predicted - actual,
            GradingCompany = company,
            AnalysisMethod = method,
            Confidence = confidence
        };

        lock (_lock)
        {
            var records = LoadRecords();
            records.Add(record);
            SaveRecords(records);
        }

        logger.LogInformation(
            "Calibration recorded: predicted={Predicted:F1} actual={Actual:F1} error={Error:F2} company={Company}",
            predicted, actual, record.Error, company);
    }

    /// <summary>
    /// Computes aggregated calibration metrics from stored records.
    /// </summary>
    public CalibrationMetrics ComputeMetrics(string? companyFilter = null)
    {
        List<CalibrationRecord> records;
        lock (_lock)
        {
            records = LoadRecords();
        }

        if (!string.IsNullOrEmpty(companyFilter))
        {
            records = records.Where(r =>
                r.GradingCompany.Equals(companyFilter, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (records.Count == 0)
        {
            return new CalibrationMetrics
            {
                MeanAbsoluteError = 0,
                MeanSignedError = 0,
                ErrorStdDev = 0,
                AccuracyWithinHalfGrade = 0,
                AccuracyWithinOneGrade = 0,
                SampleCount = 0,
                BiasCorrection = 0,
                RangeAccuracy = new Dictionary<string, double>()
            };
        }

        var errors = records.Select(r => r.Error).ToList();
        var signedErrors = records.Select(r => r.SignedError).ToList();

        var mae = errors.Average();
        var mse = signedErrors.Average();
        var errorVariance = errors.Count > 1
            ? errors.Sum(e => Math.Pow(e - mae, 2)) / (errors.Count - 1)
            : 0;

        var withinHalf = records.Count(r => r.Error <= 0.5) / (double)records.Count;
        var withinOne = records.Count(r => r.Error <= 1.0) / (double)records.Count;

        // Compute bias correction (clamped)
        var biasCorrection = -mse;
        biasCorrection = Math.Clamp(biasCorrection,
            -_opts.CalibrationMaxBiasCorrection,
            _opts.CalibrationMaxBiasCorrection);

        // Per-range accuracy
        var rangeAccuracy = ComputeRangeAccuracy(records);

        return new CalibrationMetrics
        {
            MeanAbsoluteError = Math.Round(mae, 4),
            MeanSignedError = Math.Round(mse, 4),
            ErrorStdDev = Math.Round(Math.Sqrt(errorVariance), 4),
            AccuracyWithinHalfGrade = Math.Round(withinHalf, 4),
            AccuracyWithinOneGrade = Math.Round(withinOne, 4),
            SampleCount = records.Count,
            BiasCorrection = Math.Round(biasCorrection, 4),
            RangeAccuracy = rangeAccuracy
        };
    }

    /// <summary>
    /// Applies calibration bias correction to a predicted grade.
    /// Returns the corrected grade, or the original if insufficient data.
    /// </summary>
    public double ApplyCorrection(double predicted, string company)
    {
        var metrics = ComputeMetrics(company);

        if (metrics.SampleCount < _opts.CalibrationMinSamples)
        {
            logger.LogDebug(
                "Calibration: insufficient samples ({Count}/{Min}) for {Company}, skipping correction.",
                metrics.SampleCount, _opts.CalibrationMinSamples, company);
            return predicted;
        }

        var corrected = Math.Clamp(predicted + metrics.BiasCorrection, 1.0, 10.0);

        logger.LogDebug(
            "Calibration: {Predicted:F1} + bias({Bias:F3}) = {Corrected:F1} for {Company}",
            predicted, metrics.BiasCorrection, corrected, company);

        return Math.Round(corrected, 1);
    }

    private static Dictionary<string, double> ComputeRangeAccuracy(List<CalibrationRecord> records)
    {
        var ranges = new Dictionary<string, double>();

        for (var low = 1.0; low < 10.0; low += 0.5)
        {
            var high = low + 0.5;
            var key = $"{low:F1}-{high:F1}";
            var inRange = records.Where(r => r.ActualGrade >= low && r.ActualGrade < high).ToList();

            if (inRange.Count >= 3)
            {
                ranges[key] = Math.Round(inRange.Average(r => r.Error), 4);
            }
        }

        return ranges;
    }

    private List<CalibrationRecord> LoadRecords()
    {
        if (_records is not null) return _records;

        var path = GetDataPath();
        if (!File.Exists(path))
        {
            _records = [];
            return _records;
        }

        try
        {
            var json = File.ReadAllText(path);
            _records = JsonSerializer.Deserialize<List<CalibrationRecord>>(json) ?? [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load calibration data from {Path}.", path);
            _records = [];
        }

        return _records;
    }

    private void SaveRecords(List<CalibrationRecord> records)
    {
        _records = records;

        var path = GetDataPath();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        try
        {
            var json = JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to save calibration data to {Path}.", path);
        }
    }

    private string GetDataPath() =>
        Path.Combine(_opts.CalibrationDataPath, "calibration-records.json");
}
