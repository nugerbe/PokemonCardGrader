using Microsoft.EntityFrameworkCore;
using PokemonCardGrader.Application.Interfaces;
using PokemonCardGrader.Domain.Entities;
using PokemonCardGrader.Domain.Enums;
using PokemonCardGrader.Infrastructure.Data;

namespace PokemonCardGrader.Infrastructure.Repositories;

public sealed class GradingResultRepository(ApplicationDbContext db) : IGradingResultRepository
{
    public async Task<GradingResult?> GetBySubmissionIdAsync(Guid submissionId, CancellationToken ct = default)
    {
        return await db.GradingResults.AsNoTracking()
            .FirstOrDefaultAsync(r => r.CardSubmissionId == submissionId, ct);
    }

    public async Task<List<GradingResult>> GetByCompanyAsync(GradingCompany company, CancellationToken ct = default)
    {
        return await db.GradingResults.AsNoTracking()
            .Where(r => r.Company == company)
            .ToListAsync(ct);
    }

    public async Task<int> GetCountByCompanyAsync(GradingCompany company, CancellationToken ct = default)
    {
        return await db.GradingResults.CountAsync(r => r.Company == company, ct);
    }

    public async Task<List<GradingResult>> GetTrainingDataAsync(GradingCompany company, int limit = 1000, CancellationToken ct = default)
    {
        return await db.GradingResults.AsNoTracking()
            .Where(r => r.Company == company)
            .OrderByDescending(r => r.RecordedAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<GradingResult> AddAsync(GradingResult result, CancellationToken ct = default)
    {
        await db.GradingResults.AddAsync(result, ct);
        return result;
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await db.SaveChangesAsync(ct);
    }
}
