using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PokemonCardGrader.Application.Interfaces;
using PokemonCardGrader.Domain.Entities;
using PokemonCardGrader.Infrastructure.Data;

namespace PokemonCardGrader.Infrastructure.Repositories;

public sealed class CardSubmissionRepository(
    ApplicationDbContext db,
    ILogger<CardSubmissionRepository> logger) : ICardSubmissionRepository
{
    public async Task<CardSubmission?> GetByIdAsync(Guid id, string userId, CancellationToken ct = default)
    {
        // Filtered include: each image's AnalysisRecords navigation gets only
        // the latest record (by CreatedAt). EF Core 7+ supports this and it
        // keeps the working set small even for images with hundreds of
        // historical correction rows.
        return await db.CardSubmissions
            .Include(s => s.PokemonCard)
            .Include(s => s.Images)
                .ThenInclude(i => i.AnalysisRecords.OrderByDescending(r => r.CreatedAt).Take(1))
            .Include(s => s.Estimates)
            .Include(s => s.ActualResult)
            .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId, ct);
    }

    public async Task<CardSubmission?> GetByIdReadOnlyAsync(Guid id, string userId, CancellationToken ct = default)
    {
        return await db.CardSubmissions
            .AsNoTracking()
            .Include(s => s.PokemonCard)
            .Include(s => s.Images)
                .ThenInclude(i => i.AnalysisRecords.OrderByDescending(r => r.CreatedAt).Take(1))
            .Include(s => s.Estimates)
            .Include(s => s.ActualResult)
            .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId, ct);
    }

    public async Task<CardSubmission?> GetByIdInternalAsync(Guid id, CancellationToken ct = default)
    {
        // Do NOT include Estimates — the sole production caller
        // (SetImageDerivedScoresAndEstimateAsync) replaces all estimates via
        // DeleteEstimatesAsync + SetEstimates. Loading them here would cause EF
        // to track stale entities whose DELETEs hit 0 rows and throw
        // DbUpdateConcurrencyException.
        return await db.CardSubmissions
            .FirstOrDefaultAsync(s => s.Id == id, ct);
    }

    public async Task<List<CardSubmission>> GetByUserIdAsync(string userId, int page, int pageSize, CancellationToken ct = default)
    {
        return await db.CardSubmissions
            .Include(s => s.PokemonCard)
            .Include(s => s.Estimates)
            .Include(s => s.ActualResult)
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task<int> GetCountByUserIdAsync(string userId, CancellationToken ct = default)
    {
        return await db.CardSubmissions.CountAsync(s => s.UserId == userId, ct);
    }

    public async Task<List<CardSubmission>> GetRecentByUserIdAsync(string userId, int count = 5, CancellationToken ct = default)
    {
        return await db.CardSubmissions
            .Include(s => s.PokemonCard)
            .Include(s => s.Estimates)
            .Include(s => s.ActualResult)
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedAt)
            .Take(count)
            .ToListAsync(ct);
    }

    public async Task<bool> ExistsAsync(Guid id, string userId, CancellationToken ct = default)
    {
        return await db.CardSubmissions.AnyAsync(s => s.Id == id && s.UserId == userId, ct);
    }

    public async Task<CardSubmission> AddAsync(CardSubmission submission, CancellationToken ct = default)
    {
        await db.CardSubmissions.AddAsync(submission, ct);
        return submission;
    }

    public async Task AddImageAsync(CardImage image, CancellationToken ct = default)
    {
        await db.CardImages.AddAsync(image, ct);
    }

    public async Task<CardImage?> GetImageByIdAsync(Guid imageId, CancellationToken ct = default)
    {
        return await db.CardImages.FirstOrDefaultAsync(i => i.Id == imageId, ct);
    }

    public async Task AddAnalysisRecordAsync(ImageAnalysisRecord record, CancellationToken ct = default)
    {
        await db.ImageAnalysisRecords.AddAsync(record, ct);
    }

    public async Task<ImageAnalysisRecord?> GetLatestAnalysisAsync(Guid cardImageId, CancellationToken ct = default)
    {
        return await db.ImageAnalysisRecords
            .AsNoTracking()
            .Where(r => r.CardImageId == cardImageId)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<List<CardImage>> GetImagesWithLatestAnalysisAsync(Guid submissionId, CancellationToken ct = default)
    {
        return await db.CardImages
            .AsNoTracking()
            .Where(i => i.CardSubmissionId == submissionId
                        && i.AnalysisRecords.Any())
            .Include(i => i.AnalysisRecords.OrderByDescending(r => r.CreatedAt).Take(1))
            .ToListAsync(ct);
    }

    public async Task DeleteEstimatesAsync(Guid submissionId, CancellationToken ct = default)
    {
        await db.GradeEstimates
            .Where(e => e.CardSubmissionId == submissionId)
            .ExecuteDeleteAsync(ct);
    }

    public async Task AddEstimatesAsync(IEnumerable<GradeEstimate> estimates, CancellationToken ct = default)
    {
        await db.GradeEstimates.AddRangeAsync(estimates, ct);
    }

    public Task DeleteAsync(CardSubmission submission, CancellationToken ct = default)
    {
        db.CardSubmissions.Remove(submission);
        return Task.CompletedTask;
    }

    public async Task AddCorrectionAsync(AnalysisCorrection correction, CancellationToken ct = default)
    {
        await db.AnalysisCorrections.AddAsync(correction, ct);
    }

    public async Task<List<AnalysisCorrection>> GetCorrectionsWithAdjustedBordersAsync(int maxCount = 500, CancellationToken ct = default)
    {
        return await db.AnalysisCorrections
            .AsNoTracking()
            .OrderByDescending(c => c.CreatedAt)
            .Take(maxCount)
            .ToListAsync(ct);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await db.SaveChangesAsync(ct);
    }

    public async Task SaveChangesResolvingConcurrencyAsync(CancellationToken ct = default)
    {
        const int maxAttempts = 3;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await db.SaveChangesAsync(ct);
                if (attempt > 1)
                    logger.LogInformation("Concurrency conflict resolved on attempt {Attempt}.", attempt);
                return;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                logger.LogWarning(ex,
                    "DbUpdateConcurrencyException on {Count} entry/entries (attempt {Attempt}/{Max}). Resolving with client-wins strategy.",
                    ex.Entries.Count, attempt, maxAttempts);

                if (attempt == maxAttempts)
                    throw;

                foreach (var entry in ex.Entries)
                {
                    logger.LogWarning(
                        "  Conflicting entity: {EntityType}, State: {State}",
                        entry.Entity.GetType().Name, entry.State);

                    var dbValues = await entry.GetDatabaseValuesAsync(ct);
                    if (dbValues is not null)
                    {
                        // Client wins: keep current values, update original values to match DB
                        entry.OriginalValues.SetValues(dbValues);
                    }
                    else
                    {
                        // Entity was deleted from DB — detach so SaveChanges skips it
                        entry.State = EntityState.Detached;
                    }
                }
            }
        }
    }
}
