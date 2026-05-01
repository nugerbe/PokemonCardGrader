using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCvSharp;
using PokemonCardGrader.Application.Configuration;
using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Infrastructure.ImageAnalysis;

/// <summary>
/// Logs analysis data for future ML training and continuous learning:
/// - Debug mode: original image, normalized image, and metrics JSON
/// - Continuous learning mode: structured prediction data, user corrections,
///   grade comparisons (predicted vs actual), suitable for retraining pipelines.
/// </summary>
public sealed class AnalysisDataLogger(
    IOptions<CardAnalysisOptions> options,
    ILogger<AnalysisDataLogger> logger)
{
    private readonly CardAnalysisOptions _opts = options.Value;
    private const string DebugDataDir = "training-data";
    private static readonly System.Text.Json.JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    /// <summary>
    /// Records analysis results for a single image analysis run.
    /// Saves original image, normalized image, and metrics JSON when debug is enabled.
    /// Always saves structured learning data when continuous learning is enabled.
    /// </summary>
    public void LogAnalysis(
        Guid submissionId,
        byte[] originalImageBytes,
        Mat? normalizedImage,
        ImageAnalysisResult result)
    {
        if (_opts.DebugEnabled)
            LogDebugData(submissionId, originalImageBytes, normalizedImage, result);

        if (_opts.ContinuousLearningEnabled)
            LogLearningData(submissionId, result);
    }

    /// <summary>
    /// Appends user correction data to an existing analysis log entry.
    /// </summary>
    public void LogCorrection(Guid submissionId, UserCorrection correction)
    {
        if (_opts.DebugEnabled)
            LogDebugCorrection(submissionId, correction);

        if (_opts.ContinuousLearningEnabled)
            LogLearningCorrection(submissionId, correction);
    }

    /// <summary>
    /// Records a grade comparison: what was predicted vs what the user/expert assigned.
    /// Critical for continuous learning — enables measuring prediction accuracy over time.
    /// </summary>
    public void LogGradeComparison(
        Guid submissionId,
        GradeResult? predicted,
        double? actualGrade,
        string? gradingCompany)
    {
        if (!_opts.ContinuousLearningEnabled || (!predicted.HasValue() && !actualGrade.HasValue))
            return;

        try
        {
            var dir = EnsureLearningDir(submissionId);
            var comparison = new GradeComparisonRecord
            {
                SubmissionId = submissionId,
                RecordedAt = DateTimeOffset.UtcNow,
                PredictedGrade = predicted?.Grade,
                PredictedCompany = predicted?.Company.ToString(),
                PredictedConfidence = predicted?.Confidence,
                PredictedIsRuleBased = predicted?.IsRuleBased,
                PredictedSubGrades = predicted?.SubGrades,
                ActualGrade = actualGrade,
                ActualCompany = gradingCompany,
                Error = predicted is not null && actualGrade.HasValue
                    ? Math.Abs(predicted.Grade - actualGrade.Value)
                    : null
            };

            var json = System.Text.Json.JsonSerializer.Serialize(comparison, JsonOpts);
            File.WriteAllText(Path.Combine(dir, "grade-comparison.json"), json);

            logger.LogDebug(
                "Grade comparison logged for {SubmissionId}: predicted={Predicted}, actual={Actual}, error={Error}",
                submissionId, predicted?.Grade, actualGrade, comparison.Error);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to log grade comparison for {SubmissionId}", submissionId);
        }
    }

    // ── Debug data (images + full metrics) ──

    private void LogDebugData(
        Guid submissionId, byte[] originalImageBytes,
        Mat? normalizedImage, ImageAnalysisResult result)
    {
        try
        {
            var dir = Path.Combine(_opts.DebugOutputPath, DebugDataDir, submissionId.ToString("N"));
            Directory.CreateDirectory(dir);

            File.WriteAllBytes(Path.Combine(dir, "original.jpg"), originalImageBytes);

            if (normalizedImage is not null && !normalizedImage.Empty())
                Cv2.ImWrite(Path.Combine(dir, "normalized.jpg"), normalizedImage);

            var metrics = new AnalysisMetrics
            {
                SubmissionId = submissionId,
                AnalyzedAt = result.AnalyzedAt,
                AnalysisMethod = result.AnalysisMethod,
                CenteringLR = result.DetectedCentering?.LeftRightFront,
                CenteringTB = result.DetectedCentering?.TopBottomFront,
                CornersScore = result.CornersScore,
                EdgesScore = result.EdgesScore,
                SurfaceScore = result.SurfaceScore,
                MlSurfaceScore = result.MlSurfaceScore,
                OverallConfidence = result.OverallConfidence,
                ImageQualityScore = result.ImageQualityScore,
                DefectModelUsed = result.DefectModelUsed,
                SurfaceModelUsed = result.SurfaceModelUsed,
                DefectCount = result.DetectedDefects.Count,
                MlDefectCount = result.MlDetectedDefects.Count,
                BorderLeft = result.Overlay?.BorderLines.LeftBorderX,
                BorderRight = result.Overlay?.BorderLines.RightBorderX,
                BorderTop = result.Overlay?.BorderLines.TopBorderY,
                BorderBottom = result.Overlay?.BorderLines.BottomBorderY,
                Defects = result.DetectedDefects.Select(MapDefect).ToList(),
                MlDefects = result.MlDetectedDefects.Select(MapDefect).ToList()
            };

            var json = System.Text.Json.JsonSerializer.Serialize(metrics, JsonOpts);
            File.WriteAllText(Path.Combine(dir, "metrics.json"), json);

            logger.LogDebug("Debug analysis data logged for {SubmissionId} at {Path}", submissionId, dir);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to log debug data for {SubmissionId}", submissionId);
        }
    }

    private void LogDebugCorrection(Guid submissionId, UserCorrection correction)
    {
        try
        {
            var dir = Path.Combine(_opts.DebugOutputPath, DebugDataDir, submissionId.ToString("N"));
            if (!Directory.Exists(dir))
            {
                logger.LogDebug("No debug data directory for {SubmissionId}, skipping.", submissionId);
                return;
            }

            var correctionData = new CorrectionRecord
            {
                CorrectedAt = DateTimeOffset.UtcNow,
                HasAdjustedBoundary = correction.AdjustedBoundary is { Count: > 0 },
                HasAdjustedBorders = correction.AdjustedBorders is not null,
                DismissedDefectCount = correction.DismissedDefectIndices.Count,
                AdjustedBorderLeft = correction.AdjustedBorders?.LeftBorderX,
                AdjustedBorderRight = correction.AdjustedBorders?.RightBorderX,
                AdjustedBorderTop = correction.AdjustedBorders?.TopBorderY,
                AdjustedBorderBottom = correction.AdjustedBorders?.BottomBorderY
            };

            var json = System.Text.Json.JsonSerializer.Serialize(correctionData, JsonOpts);
            File.WriteAllText(Path.Combine(dir, "correction.json"), json);

            logger.LogDebug("Debug correction logged for {SubmissionId}", submissionId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to log debug correction for {SubmissionId}", submissionId);
        }
    }

    // ── Continuous learning data (lightweight, always-on) ──

    private void LogLearningData(Guid submissionId, ImageAnalysisResult result)
    {
        try
        {
            var dir = EnsureLearningDir(submissionId);

            var record = new LearningRecord
            {
                SubmissionId = submissionId,
                AnalyzedAt = result.AnalyzedAt,
                AnalysisMethod = result.AnalysisMethod,
                CenteringLR = result.DetectedCentering?.LeftRightFront,
                CenteringTB = result.DetectedCentering?.TopBottomFront,
                CenteringMaxDeviation = result.DetectedCentering?.MaxDeviation,
                CornersScore = result.CornersScore,
                EdgesScore = result.EdgesScore,
                SurfaceScore = result.SurfaceScore,
                MlSurfaceScore = result.MlSurfaceScore,
                OverallConfidence = result.OverallConfidence,
                ImageQualityScore = result.ImageQualityScore,
                DefectModelUsed = result.DefectModelUsed,
                SurfaceModelUsed = result.SurfaceModelUsed,
                CvDefectCount = result.DetectedDefects.Count,
                MlDefectCount = result.MlDetectedDefects.Count,
                CvDefectTypes = result.DetectedDefects.Select(d => d.Type).Distinct().ToList(),
                MlDefectTypes = result.MlDetectedDefects.Select(d => d.Type).Distinct().ToList(),
                CvDefectSeverityMax = result.DetectedDefects.Count > 0
                    ? result.DetectedDefects.Max(d => d.Severity) : null,
                MlDefectConfidenceMax = result.MlDetectedDefects.Count > 0
                    ? result.MlDetectedDefects.Max(d => d.Confidence) : null,
                ConfidenceImageQuality = result.ConfidenceDetail?.ImageQuality,
                ConfidenceDetection = result.ConfidenceDetail?.DetectionReliability,
                ConfidenceCvMl = result.ConfidenceDetail?.CvMlAgreement,
                ConfidenceBorder = result.ConfidenceDetail?.BorderConsistency
            };

            var json = System.Text.Json.JsonSerializer.Serialize(record, JsonOpts);
            File.WriteAllText(Path.Combine(dir, "prediction.json"), json);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to log learning data for {SubmissionId}", submissionId);
        }
    }

    private void LogLearningCorrection(Guid submissionId, UserCorrection correction)
    {
        try
        {
            var dir = EnsureLearningDir(submissionId);

            var record = new LearningCorrectionRecord
            {
                CorrectedAt = DateTimeOffset.UtcNow,
                HasAdjustedBorders = correction.AdjustedBorders is not null,
                HasAdjustedBoundary = correction.AdjustedBoundary is { Count: > 0 },
                DismissedDefectCount = correction.DismissedDefectIndices.Count,
                DismissedIndices = correction.DismissedDefectIndices,
                AdjustedBorderLeft = correction.AdjustedBorders?.LeftBorderX,
                AdjustedBorderRight = correction.AdjustedBorders?.RightBorderX,
                AdjustedBorderTop = correction.AdjustedBorders?.TopBorderY,
                AdjustedBorderBottom = correction.AdjustedBorders?.BottomBorderY
            };

            var json = System.Text.Json.JsonSerializer.Serialize(record, JsonOpts);
            File.WriteAllText(Path.Combine(dir, "correction.json"), json);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to log learning correction for {SubmissionId}", submissionId);
        }
    }

    private string EnsureLearningDir(Guid submissionId)
    {
        var dir = Path.Combine(_opts.LearningDataPath, submissionId.ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static DefectRecord MapDefect(DetectedDefect d) => new()
    {
        Type = d.Type,
        Severity = d.Severity,
        Confidence = d.Confidence,
        X = d.X,
        Y = d.Y,
        Width = d.Width,
        Height = d.Height
    };

    // ── Serialization DTOs ──

    private sealed class AnalysisMetrics
    {
        public Guid SubmissionId { get; init; }
        public DateTimeOffset AnalyzedAt { get; init; }
        public string AnalysisMethod { get; init; } = "";
        public double? CenteringLR { get; init; }
        public double? CenteringTB { get; init; }
        public double? CornersScore { get; init; }
        public double? EdgesScore { get; init; }
        public double? SurfaceScore { get; init; }
        public double? MlSurfaceScore { get; init; }
        public double? OverallConfidence { get; init; }
        public double? ImageQualityScore { get; init; }
        public bool DefectModelUsed { get; init; }
        public bool SurfaceModelUsed { get; init; }
        public int DefectCount { get; init; }
        public int MlDefectCount { get; init; }
        public double? BorderLeft { get; init; }
        public double? BorderRight { get; init; }
        public double? BorderTop { get; init; }
        public double? BorderBottom { get; init; }
        public List<DefectRecord> Defects { get; init; } = [];
        public List<DefectRecord> MlDefects { get; init; } = [];
    }

    private sealed class DefectRecord
    {
        public string Type { get; init; } = "";
        public double Severity { get; init; }
        public double Confidence { get; init; }
        public double X { get; init; }
        public double Y { get; init; }
        public double Width { get; init; }
        public double Height { get; init; }
    }

    private sealed class CorrectionRecord
    {
        public DateTimeOffset CorrectedAt { get; init; }
        public bool HasAdjustedBoundary { get; init; }
        public bool HasAdjustedBorders { get; init; }
        public int DismissedDefectCount { get; init; }
        public double? AdjustedBorderLeft { get; init; }
        public double? AdjustedBorderRight { get; init; }
        public double? AdjustedBorderTop { get; init; }
        public double? AdjustedBorderBottom { get; init; }
    }

    private sealed class LearningRecord
    {
        public Guid SubmissionId { get; init; }
        public DateTimeOffset AnalyzedAt { get; init; }
        public string AnalysisMethod { get; init; } = "";
        public double? CenteringLR { get; init; }
        public double? CenteringTB { get; init; }
        public double? CenteringMaxDeviation { get; init; }
        public double? CornersScore { get; init; }
        public double? EdgesScore { get; init; }
        public double? SurfaceScore { get; init; }
        public double? MlSurfaceScore { get; init; }
        public double? OverallConfidence { get; init; }
        public double? ImageQualityScore { get; init; }
        public bool DefectModelUsed { get; init; }
        public bool SurfaceModelUsed { get; init; }
        public int CvDefectCount { get; init; }
        public int MlDefectCount { get; init; }
        public List<string> CvDefectTypes { get; init; } = [];
        public List<string> MlDefectTypes { get; init; } = [];
        public double? CvDefectSeverityMax { get; init; }
        public double? MlDefectConfidenceMax { get; init; }
        public double? ConfidenceImageQuality { get; init; }
        public double? ConfidenceDetection { get; init; }
        public double? ConfidenceCvMl { get; init; }
        public double? ConfidenceBorder { get; init; }
    }

    private sealed class LearningCorrectionRecord
    {
        public DateTimeOffset CorrectedAt { get; init; }
        public bool HasAdjustedBorders { get; init; }
        public bool HasAdjustedBoundary { get; init; }
        public int DismissedDefectCount { get; init; }
        public List<int> DismissedIndices { get; init; } = [];
        public double? AdjustedBorderLeft { get; init; }
        public double? AdjustedBorderRight { get; init; }
        public double? AdjustedBorderTop { get; init; }
        public double? AdjustedBorderBottom { get; init; }
    }

    private sealed class GradeComparisonRecord
    {
        public Guid SubmissionId { get; init; }
        public DateTimeOffset RecordedAt { get; init; }
        public double? PredictedGrade { get; init; }
        public string? PredictedCompany { get; init; }
        public double? PredictedConfidence { get; init; }
        public bool? PredictedIsRuleBased { get; init; }
        public Dictionary<string, double>? PredictedSubGrades { get; init; }
        public double? ActualGrade { get; init; }
        public string? ActualCompany { get; init; }
        public double? Error { get; init; }
    }
}

file static class GradeResultExtensions
{
    public static bool HasValue(this GradeResult? grade) => grade is not null;
}
