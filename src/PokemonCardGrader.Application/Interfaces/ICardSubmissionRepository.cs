using PokemonCardGrader.Domain.Entities;

namespace PokemonCardGrader.Application.Interfaces;

public interface ICardSubmissionRepository
{
    Task AddCorrectionAsync(AnalysisCorrection correction, CancellationToken ct = default);

    /// <summary>
    /// Returns corrections that include user-adjusted border positions, for training border prediction.
    /// </summary>
    Task<List<AnalysisCorrection>> GetCorrectionsWithAdjustedBordersAsync(int maxCount = 500, CancellationToken ct = default);
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
    void DetachImage(CardImage image);
    void ReattachImageAsModified(CardImage image);
    Task<List<CardImage>> GetAnalyzedImagesBySubmissionAsync(Guid submissionId, CancellationToken ct = default);
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
