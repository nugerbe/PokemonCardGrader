using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PokemonCardGrader.Application.Configuration;
using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Infrastructure.ML;

/// <summary>
/// Phase 19: Processes user feedback (grade overrides, defect confirmations/rejections)
/// and stores it for future model retraining. Tracks feedback statistics and
/// generates training-ready data from accumulated corrections.
/// </summary>
public sealed class UserFeedbackService(
    IOptions<CardAnalysisOptions> options,
    CalibrationService calibrationService,
    ILogger<UserFeedbackService> logger)
{
    private readonly CardAnalysisOptions _opts = options.Value;
    private readonly Lock _lock = new();

    public sealed record FeedbackRecord(
        Guid SubmissionId,
        DateTimeOffset RecordedAt,
        string FeedbackType,
        double? PredictedGrade,
        double? ActualGrade,
        string? GradingCompany,
        List<int> ConfirmedDefectIndices,
        List<int> RejectedDefectIndices,
        string? UserNotes);

    public sealed record FeedbackStats(
        int TotalFeedbacks,
        int GradeOverrides,
        int DefectConfirmations,
        int DefectRejections,
        double AverageGradeCorrection,
        double DefectAcceptanceRate);

    /// <summary>
    /// Records a grade override from a user who received professional grading.
    /// </summary>
    public void RecordGradeOverride(
        Guid submissionId, double predictedGrade, double actualGrade,
        string company, string analysisMethod, double confidence, string? notes = null)
    {
        var record = new FeedbackRecord(
            SubmissionId: submissionId,
            RecordedAt: DateTimeOffset.UtcNow,
            FeedbackType: "grade_override",
            PredictedGrade: predictedGrade,
            ActualGrade: actualGrade,
            GradingCompany: company,
            ConfirmedDefectIndices: [],
            RejectedDefectIndices: [],
            UserNotes: notes);

        SaveFeedback(record);

        // Feed into calibration system
        calibrationService.RecordCalibration(
            submissionId, predictedGrade, actualGrade, company, analysisMethod, confidence);

        logger.LogInformation(
            "Grade override recorded: predicted={Predicted:F1} actual={Actual:F1} company={Company}",
            predictedGrade, actualGrade, company);
    }

    /// <summary>
    /// Records defect confirmations/rejections from user correction.
    /// </summary>
    public void RecordDefectFeedback(
        Guid submissionId, List<int> confirmedIndices, List<int> rejectedIndices,
        string? notes = null)
    {
        var record = new FeedbackRecord(
            SubmissionId: submissionId,
            RecordedAt: DateTimeOffset.UtcNow,
            FeedbackType: "defect_feedback",
            PredictedGrade: null,
            ActualGrade: null,
            GradingCompany: null,
            ConfirmedDefectIndices: confirmedIndices,
            RejectedDefectIndices: rejectedIndices,
            UserNotes: notes);

        SaveFeedback(record);

        logger.LogInformation(
            "Defect feedback recorded: confirmed={Confirmed} rejected={Rejected}",
            confirmedIndices.Count, rejectedIndices.Count);
    }

    /// <summary>
    /// Computes feedback statistics from accumulated records.
    /// </summary>
    public FeedbackStats GetStatistics()
    {
        var records = LoadAllFeedback();

        if (records.Count == 0)
        {
            return new FeedbackStats(0, 0, 0, 0, 0, 1.0);
        }

        var gradeOverrides = records.Where(r => r.FeedbackType == "grade_override").ToList();
        var defectFeedbacks = records.Where(r => r.FeedbackType == "defect_feedback").ToList();

        var avgCorrection = gradeOverrides.Count > 0
            ? gradeOverrides
                .Where(r => r.PredictedGrade.HasValue && r.ActualGrade.HasValue)
                .Average(r => Math.Abs(r.PredictedGrade!.Value - r.ActualGrade!.Value))
            : 0;

        var totalConfirmed = defectFeedbacks.Sum(r => r.ConfirmedDefectIndices.Count);
        var totalRejected = defectFeedbacks.Sum(r => r.RejectedDefectIndices.Count);
        var totalDefectFeedback = totalConfirmed + totalRejected;
        var acceptanceRate = totalDefectFeedback > 0
            ? (double)totalConfirmed / totalDefectFeedback
            : 1.0;

        return new FeedbackStats(
            TotalFeedbacks: records.Count,
            GradeOverrides: gradeOverrides.Count,
            DefectConfirmations: totalConfirmed,
            DefectRejections: totalRejected,
            AverageGradeCorrection: Math.Round(avgCorrection, 4),
            DefectAcceptanceRate: Math.Round(acceptanceRate, 4));
    }

    /// <summary>
    /// Returns all grade override records, suitable for retraining input.
    /// </summary>
    public IReadOnlyList<FeedbackRecord> GetTrainingData()
    {
        return LoadAllFeedback()
            .Where(r => r.FeedbackType == "grade_override" &&
                        r.PredictedGrade.HasValue && r.ActualGrade.HasValue)
            .ToList()
            .AsReadOnly();
    }

    private void SaveFeedback(FeedbackRecord record)
    {
        lock (_lock)
        {
            var records = LoadAllFeedback();
            records.Add(record);
            PersistFeedback(records);
        }
    }

    private List<FeedbackRecord> LoadAllFeedback()
    {
        var path = GetDataPath();
        if (!File.Exists(path)) return [];

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<FeedbackRecord>>(json) ?? [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load feedback data from {Path}.", path);
            return [];
        }
    }

    private void PersistFeedback(List<FeedbackRecord> records)
    {
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
            logger.LogWarning(ex, "Failed to persist feedback data to {Path}.", path);
        }
    }

    private string GetDataPath() =>
        Path.Combine(_opts.CalibrationDataPath, "user-feedback.json");
}
