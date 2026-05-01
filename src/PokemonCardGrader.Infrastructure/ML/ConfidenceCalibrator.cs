using Microsoft.Extensions.Logging;
using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Infrastructure.ML;

/// <summary>
/// Phase 20: Tracks confidence vs correctness over time and recalibrates
/// confidence scores using isotonic regression (Platt scaling approximation).
/// Ensures that a confidence of 0.8 means ~80% of predictions are correct.
/// </summary>
public sealed class ConfidenceCalibrator(
    CalibrationService calibrationService,
    ILogger<ConfidenceCalibrator> logger)
{
    private readonly Lock _lock = new();
    private List<(double Confidence, bool Correct)>? _history;

    /// <summary>
    /// Records a confidence-correctness pair for calibration tracking.
    /// </summary>
    public void RecordOutcome(double confidence, bool wasCorrect)
    {
        lock (_lock)
        {
            _history ??= [];
            _history.Add((confidence, wasCorrect));

            // Limit history size to prevent unbounded growth
            if (_history.Count > 10000)
            {
                _history.RemoveRange(0, _history.Count - 10000);
            }
        }
    }

    /// <summary>
    /// Calibrates a raw confidence score based on historical reliability data.
    /// Returns the calibrated confidence that more accurately reflects true probability.
    /// </summary>
    public double Calibrate(double rawConfidence)
    {
        List<(double Confidence, bool Correct)> history;
        lock (_lock)
        {
            if (_history is null || _history.Count < 30)
            {
                return rawConfidence; // Insufficient data for calibration
            }
            history = new List<(double Confidence, bool Correct)>(_history);
        }

        // Bin-based calibration (isotonic regression approximation)
        const int bins = 10;
        var binCorrect = new double[bins];
        var binCount = new int[bins];

        foreach (var (conf, correct) in history)
        {
            var bin = Math.Min((int)(conf * bins), bins - 1);
            binCorrect[bin] += correct ? 1.0 : 0.0;
            binCount[bin]++;
        }

        // Compute calibrated values for each bin
        var calibratedBins = new double[bins];
        for (var b = 0; b < bins; b++)
        {
            calibratedBins[b] = binCount[b] >= 5
                ? binCorrect[b] / binCount[b]
                : (b + 0.5) / bins; // Default to uniform if insufficient data
        }

        // Apply isotonic constraint (non-decreasing)
        for (var b = 1; b < bins; b++)
        {
            if (calibratedBins[b] < calibratedBins[b - 1])
            {
                // Pool adjacent bins
                var pooledCorrect = binCorrect[b - 1] + binCorrect[b];
                var pooledCount = binCount[b - 1] + binCount[b];
                var pooledRate = pooledCount > 0 ? pooledCorrect / pooledCount : calibratedBins[b - 1];
                calibratedBins[b - 1] = pooledRate;
                calibratedBins[b] = pooledRate;
            }
        }

        // Interpolate for the given raw confidence
        var rawBin = rawConfidence * bins;
        var lowerBin = Math.Min((int)rawBin, bins - 1);
        var upperBin = Math.Min(lowerBin + 1, bins - 1);
        var frac = rawBin - lowerBin;

        var calibrated = calibratedBins[lowerBin] * (1 - frac) + calibratedBins[upperBin] * frac;
        calibrated = Math.Clamp(calibrated, 0.01, 0.99);

        if (Math.Abs(calibrated - rawConfidence) > 0.1)
        {
            logger.LogDebug(
                "Confidence calibration: raw={Raw:F3} -> calibrated={Calibrated:F3} (delta={Delta:F3})",
                rawConfidence, calibrated, calibrated - rawConfidence);
        }

        return calibrated;
    }

    /// <summary>
    /// Computes and returns calibration reliability metrics.
    /// </summary>
    public ConfidenceReliability GetReliability()
    {
        List<(double Confidence, bool Correct)> history;
        lock (_lock)
        {
            if (_history is null || _history.Count == 0)
            {
                return new ConfidenceReliability(0, 0, 0, []);
            }
            history = new List<(double Confidence, bool Correct)>(_history);
        }

        // Compute per-bin reliability
        const int bins = 5;
        var binReliability = new Dictionary<string, double>();

        for (var b = 0; b < bins; b++)
        {
            var low = (double)b / bins;
            var high = (double)(b + 1) / bins;
            var inBin = history.Where(h => h.Confidence >= low && h.Confidence < high).ToList();

            if (inBin.Count >= 5)
            {
                var avgConf = inBin.Average(h => h.Confidence);
                var actualRate = inBin.Average(h => h.Correct ? 1.0 : 0.0);
                binReliability[$"{low:F1}-{high:F1}"] = Math.Round(Math.Abs(avgConf - actualRate), 4);
            }
        }

        // Overall ECE
        var ece = binReliability.Count > 0 ? binReliability.Values.Average() : 0;
        var overallAccuracy = history.Average(h => h.Correct ? 1.0 : 0.0);

        return new ConfidenceReliability(
            SampleCount: history.Count,
            ExpectedCalibrationError: Math.Round(ece, 4),
            OverallAccuracy: Math.Round(overallAccuracy, 4),
            BinReliability: binReliability);
    }

    public sealed record ConfidenceReliability(
        int SampleCount,
        double ExpectedCalibrationError,
        double OverallAccuracy,
        Dictionary<string, double> BinReliability);
}
