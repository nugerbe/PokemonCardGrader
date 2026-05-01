using PokemonCardGrader.Application.DTOs;
using PokemonCardGrader.Application.Interfaces;
using PokemonCardGrader.Domain.Entities;
using PokemonCardGrader.Domain.Enums;
using PokemonCardGrader.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace PokemonCardGrader.Application.Services;

public sealed class CardSubmissionService(
    ICardSubmissionRepository submissionRepository,
    IGradeEstimationService estimationService,
    IGradingResultRepository gradingResultRepository,
    IImageStorageService imageStorageService,
    IImageAnalysisService imageAnalysisService,
    ILogger<CardSubmissionService> logger)
{
    public async Task<CardSubmission> CreateSubmissionAsync(
        string userId, Guid pokemonCardId, string? notes = null, CancellationToken ct = default)
    {
        var submission = CardSubmission.Create(userId, pokemonCardId, notes);
        await submissionRepository.AddAsync(submission, ct);
        await submissionRepository.SaveChangesAsync(ct);
        return submission;
    }

    public async Task<CardSubmission?> GetSubmissionAsync(Guid id, string userId, CancellationToken ct = default)
    {
        return await submissionRepository.GetByIdAsync(id, userId, ct);
    }

    public async Task<CardSubmission?> GetSubmissionReadOnlyAsync(Guid id, string userId, CancellationToken ct = default)
    {
        return await submissionRepository.GetByIdReadOnlyAsync(id, userId, ct);
    }

    public async Task<List<CardSubmission>> GetUserSubmissionsAsync(
        string userId, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        return await submissionRepository.GetByUserIdAsync(userId, page, pageSize, ct);
    }

    public async Task SetManualScoresAndEstimateAsync(
        Guid submissionId, string userId, ConditionScores scores, CancellationToken ct = default)
    {
        var submission = await submissionRepository.GetByIdAsync(submissionId, userId, ct)
            ?? throw new InvalidOperationException("Submission not found.");

        submission.SetManualScores(scores);

        // Use concurrency-safe save because the background worker may have
        // updated ImageDerivedScores/FinalScores (changing the row version)
        // between when we loaded the entity and when we save here.
        await submissionRepository.SaveChangesResolvingConcurrencyAsync(ct);

        if (submission.FinalScores is not null)
        {
            await submissionRepository.DeleteEstimatesAsync(submissionId, ct);

            var estimates = await estimationService.EstimateAllCompaniesAsync(
                submissionId, submission.FinalScores, ct);
            await submissionRepository.AddEstimatesAsync(estimates, ct);
            await submissionRepository.SaveChangesAsync(ct);
        }
    }

    public async Task SetFinalScoresAndEstimateAsync(
        Guid submissionId, string userId, ConditionScores scores, CancellationToken ct = default)
    {
        var submission = await submissionRepository.GetByIdAsync(submissionId, userId, ct)
            ?? throw new InvalidOperationException("Submission not found.");

        submission.SetFinalScores(scores);

        // Use concurrency-safe save because the background worker may have
        // updated ImageDerivedScores/FinalScores (changing the row version)
        // between when we loaded the entity and when we save here.
        await submissionRepository.SaveChangesResolvingConcurrencyAsync(ct);

        // Replace estimates via direct SQL delete + bulk insert so the change
        // tracker never holds stale GradeEstimate entities from a prior state.
        await submissionRepository.DeleteEstimatesAsync(submissionId, ct);

        var estimates = await estimationService.EstimateAllCompaniesAsync(
            submissionId, scores, ct);
        await submissionRepository.AddEstimatesAsync(estimates, ct);
        await submissionRepository.SaveChangesAsync(ct);
    }

    public async Task SetImageDerivedScoresAndEstimateAsync(
        Guid submissionId, ConditionScores scores, CancellationToken ct = default)
    {
        var submission = await submissionRepository.GetByIdInternalAsync(submissionId, ct)
            ?? throw new InvalidOperationException("Submission not found.");

        submission.SetImageDerivedScores(scores);

        // Save score changes first, isolated from estimate modifications.
        // ToJson() owned entities (ImageDerivedScores, FinalScores) transitioning
        // from NULL to a value can trigger DbUpdateConcurrencyException.
        await submissionRepository.SaveChangesResolvingConcurrencyAsync(ct);

        if (submission.FinalScores is not null)
        {
            // Replace estimates via direct SQL delete + bulk insert so the change
            // tracker never holds stale GradeEstimate entities from a prior state.
            await submissionRepository.DeleteEstimatesAsync(submissionId, ct);

            var estimates = await estimationService.EstimateAllCompaniesAsync(
                submissionId, submission.FinalScores, ct);
            await submissionRepository.AddEstimatesAsync(estimates, ct);
            await submissionRepository.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Combines scores from all analyzed images for a submission.
    /// Front image → LR_Front, TB_Front, Corners, Edges, Surface.
    /// Back image → LR_Back, TB_Back (condition scores averaged with front if both exist).
    /// Always re-combines ALL analyzed images — order doesn't matter.
    /// </summary>
    public async Task CombineAndSetImageScoresAsync(Guid submissionId, CancellationToken ct = default)
    {
        var analyzedImages = await submissionRepository.GetImagesWithLatestAnalysisAsync(submissionId, ct);

        if (analyzedImages.Count == 0)
        {
            logger.LogWarning("CombineAndSetImageScoresAsync: no analyzed images for submission {SubmissionId}.", submissionId);
            return;
        }

        var frontImage = analyzedImages.FirstOrDefault(i => i.ImageType == ImageType.Front);
        var backImage = analyzedImages.FirstOrDefault(i => i.ImageType == ImageType.Back);

        var frontResult = frontImage?.LatestAnalysisResult;
        var backResult = backImage?.LatestAnalysisResult;

        // Centering: front image provides LR_Front/TB_Front, back image provides LR_Back/TB_Back
        var lrFront = frontResult?.DetectedCentering?.LeftRightFront ?? 50.0;
        var tbFront = frontResult?.DetectedCentering?.TopBottomFront ?? 50.0;
        var lrBack = backResult?.DetectedCentering?.LeftRightFront ?? 50.0; // Back image's centering → LR_Back
        var tbBack = backResult?.DetectedCentering?.TopBottomFront ?? 50.0; // Back image's centering → TB_Back

        // Condition scores: combine front and back if both available
        double corners, edges, surface;
        if (frontResult is not null && backResult is not null)
        {
            corners = ((frontResult.CornersScore ?? 8.0) + (backResult.CornersScore ?? 8.0)) / 2.0;
            edges = ((frontResult.EdgesScore ?? 8.0) + (backResult.EdgesScore ?? 8.0)) / 2.0;
            surface = ((frontResult.SurfaceScore ?? 8.0) + (backResult.SurfaceScore ?? 8.0)) / 2.0;

            // Round to nearest 0.5
            corners = Math.Round(corners * 2) / 2.0;
            edges = Math.Round(edges * 2) / 2.0;
            surface = Math.Round(surface * 2) / 2.0;
        }
        else
        {
            var source = frontResult ?? backResult!;
            corners = source.CornersScore ?? 8.0;
            edges = source.EdgesScore ?? 8.0;
            surface = source.SurfaceScore ?? 8.0;
        }

        var combinedScores = new ConditionScores
        {
            Centering = new CenteringMeasurement
            {
                LeftRightFront = lrFront,
                TopBottomFront = tbFront,
                LeftRightBack = lrBack,
                TopBottomBack = tbBack
            },
            Corners = Math.Clamp(corners, 1.0, 10.0),
            Edges = Math.Clamp(edges, 1.0, 10.0),
            Surface = Math.Clamp(surface, 1.0, 10.0)
        };

        logger.LogInformation(
            "Combined image scores for submission {SubmissionId}: " +
            "LR_Front={LRFront:F1}% LR_Back={LRBack:F1}% TB_Front={TBFront:F1}% TB_Back={TBBack:F1}% " +
            "Corners={Corners:F2} Edges={Edges:F2} Surface={Surface:F2} (front={HasFront}, back={HasBack})",
            submissionId, lrFront, lrBack, tbFront, tbBack,
            combinedScores.Corners, combinedScores.Edges, combinedScores.Surface,
            frontResult is not null, backResult is not null);

        await SetImageDerivedScoresAndEstimateAsync(submissionId, combinedScores, ct);
    }

    public async Task ApplyUserCorrectionAsync(
        Guid submissionId, string userId, UserCorrection correction, CancellationToken ct = default)
    {
        // Verify submission belongs to user
        var submission = await submissionRepository.GetByIdAsync(submissionId, userId, ct)
            ?? throw new InvalidOperationException("Submission not found.");

        // Find the image inside the loaded submission graph (which includes the
        // latest analysis record per image via filtered include). Drawing the
        // image FROM the submission also gives us the submission-ownership check
        // for free — no need for a separate CardSubmissionId comparison.
        var image = submission.Images.FirstOrDefault(i => i.Id == correction.CardImageId)
            ?? throw new InvalidOperationException("Image not found.");

        var latestResult = image.LatestAnalysisResult
            ?? throw new InvalidOperationException("Image has not been analyzed yet.");

        // Recalculate scores from the correction. Note: this returns a NEW
        // ImageAnalysisResult built off the previous one — append-only model
        // means we never mutate `latestResult`, we just write the new one.
        // The previous Initial-source record stays in place; the diff between
        // it and this new UserCorrection-source row IS the supervised
        // training signal consumed by BorderPredictionService.
        var updatedResult = imageAnalysisService.RecalculateFromCorrection(
            latestResult, correction);

        var newRecord = ImageAnalysisRecord.Create(
            image.Id,
            updatedResult,
            AnalysisRecordSource.UserCorrection);
        await submissionRepository.AddAnalysisRecordAsync(newRecord, ct);

        await submissionRepository.SaveChangesResolvingConcurrencyAsync(ct);

        // Re-combine all image scores and re-estimate. The new
        // UserCorrection-source ImageAnalysisRecord is now the audit-trail
        // and training-data row in one — BorderPredictionService reads
        // these directly via GetRecentUserCorrectionRecordsAsync.
        await CombineAndSetImageScoresAsync(submissionId, ct);
    }

    public async Task DeleteSubmissionAsync(Guid id, string userId, CancellationToken ct = default)
    {
        var submission = await submissionRepository.GetByIdAsync(id, userId, ct)
            ?? throw new InvalidOperationException("Submission not found.");

        foreach (var image in submission.Images)
        {
            await imageStorageService.DeleteImageAsync(image.StoragePath, ct);
        }

        await submissionRepository.DeleteAsync(submission, ct);
        await submissionRepository.SaveChangesAsync(ct);
    }

    public async Task RecordActualGradeAsync(
        Guid submissionId,
        string userId,
        GradingCompany company,
        double actualGrade,
        Dictionary<string, double> actualSubGrades,
        string? certificationNumber,
        CancellationToken ct = default)
    {
        var submission = await submissionRepository.GetByIdAsync(submissionId, userId, ct)
            ?? throw new InvalidOperationException("Submission not found.");

        var result = GradingResult.Create(
            submissionId, userId, company, actualGrade, actualSubGrades, certificationNumber);

        submission.RecordActualResult(result);
        await gradingResultRepository.AddAsync(result, ct);
        await submissionRepository.SaveChangesAsync(ct);
    }

    public CardSubmissionDto MapToDto(CardSubmission submission)
    {
        return new CardSubmissionDto
        {
            Id = submission.Id,
            CardName = submission.PokemonCard.Name,
            SetName = submission.PokemonCard.SetName,
            Number = submission.PokemonCard.Number,
            CardImageUrl = submission.PokemonCard.ImageUrl,
            ManualScores = submission.ManualScores,
            ImageDerivedScores = submission.ImageDerivedScores,
            FinalScores = submission.FinalScores,
            Estimates = submission.Estimates.Select(e => new GradeEstimateDto
            {
                Company = e.Company,
                PredictedGrade = e.PredictedGrade,
                SubGrades = e.SubGrades,
                Confidence = e.Confidence,
                IsRuleBased = e.IsRuleBased,
                Label = e.Label
            }).ToList(),
            Images = submission.Images.Select(img => new CardImageDto
            {
                Id = img.Id,
                ImageUrl = imageStorageService.GetImageUrl(img.StoragePath),
                ImageType = img.ImageType,
                IsAnalyzed = img.LatestAnalysis is not null,
                Overlay = img.LatestAnalysisResult?.Overlay,
                DetectedDefects = img.LatestAnalysisResult?.DetectedDefects,
                UploadedAt = img.UploadedAt
            }).ToList(),
            ActualResult = submission.ActualResult is not null
                ? new GradingResultDto
                {
                    Company = submission.ActualResult.Company,
                    ActualGrade = submission.ActualResult.ActualGrade,
                    ActualSubGrades = submission.ActualResult.ActualSubGrades,
                    CertificationNumber = submission.ActualResult.CertificationNumber,
                    RecordedAt = submission.ActualResult.RecordedAt
                }
                : null,
            CreatedAt = submission.CreatedAt
        };
    }
}
