using PokemonCardGrader.Domain.Entities;
using PokemonCardGrader.Domain.Enums;

namespace PokemonCardGrader.Application.Interfaces;

public interface IGradingResultRepository
{
    Task<GradingResult?> GetBySubmissionIdAsync(Guid submissionId, CancellationToken ct = default);
    Task<List<GradingResult>> GetByCompanyAsync(GradingCompany company, CancellationToken ct = default);
    Task<int> GetCountByCompanyAsync(GradingCompany company, CancellationToken ct = default);
    Task<List<GradingResult>> GetTrainingDataAsync(GradingCompany company, int limit = 1000, CancellationToken ct = default);
    Task<GradingResult> AddAsync(GradingResult result, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
