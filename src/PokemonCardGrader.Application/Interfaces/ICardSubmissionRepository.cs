using PokemonCardGrader.Domain.Entities;

namespace PokemonCardGrader.Application.Interfaces;

public interface ICardSubmissionRepository
{
    /// <summary>
    /// Returns recent <see cref="ImageAnalysisRecord"/> rows where the source
    /// is <see cref="Domain.Enums.AnalysisRecordSource.UserCorrection"/>.
    /// These ARE the first-class corrections — the diff between an Initial
    /// record and a subsequent UserCorrection record on the same image is
    /// the supervised training signal for border-prediction priors and
    /// future ML models.
    /// </summary>
    Task<List<ImageAnalysisRecord>> GetRecentUserCorrectionRecordsAsync(int maxCount = 500, CancellationToken ct = default);
    Task<CardSubmission?> GetByIdAsync(Guid id, string userId, CancellationToken ct = default);
    Task<CardSubmission?> GetByIdReadOnlyAsync(Guid id, string userId, CancellationToken ct = default);
    Task<CardSubmission?> GetByIdInternalAsync(Guid id, CancellationToken ct = default);
    Task<List<CardSubmission>> GetByUserIdAsync(string userId, int page, int pageSize, CancellationToken ct = default);
    Task<int> GetCountByUserIdAsync(string userId, CancellationToken ct = default);
    Task<List<CardSubmission>> GetRecentByUserIdAsync(string userId, int count = 5, CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid id, string userId, CancellationToken ct = default);
    Task<CardSubmission> AddAsync(CardSubmission submission, CancellationToken ct = default);
    Task AddImageAsync(CardImage image, CancellationToken ct = default);
    Task<CardImage?> GetImageByIdAsync(Guid imageId, CancellationToken ct = default);

    /// <summary>
    /// Append a new analysis record for an image. Records are immutable once
    /// written; corrections add a new row rather than mutating the previous one.
    /// </summary>
    Task AddAnalysisRecordAsync(ImageAnalysisRecord record, CancellationToken ct = default);

    /// <summary>
    /// Latest (by CreatedAt) analysis record for the given image, or null if
    /// the image has not been analysed yet.
    /// </summary>
    Task<ImageAnalysisRecord?> GetLatestAnalysisAsync(Guid cardImageId, CancellationToken ct = default);

    /// <summary>
    /// Loads each image of the submission with its latest analysis record
    /// pre-included. Only images that have at least one analysis record are
    /// returned. Read-only / no-tracking.
    /// </summary>
    Task<List<CardImage>> GetImagesWithLatestAnalysisAsync(Guid submissionId, CancellationToken ct = default);
    Task DeleteEstimatesAsync(Guid submissionId, CancellationToken ct = default);
    Task AddEstimatesAsync(IEnumerable<GradeEstimate> estimates, CancellationToken ct = default);
    Task DeleteAsync(CardSubmission submission, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);

    /// <summary>
    /// Saves changes, automatically resolving any <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/>
    /// with a client-wins strategy (reload original values from DB, keep current values, re-save).
    /// Use for background/worker operations where the caller's values should always prevail.
    /// </summary>
    Task SaveChangesResolvingConcurrencyAsync(CancellationToken ct = default);
}
