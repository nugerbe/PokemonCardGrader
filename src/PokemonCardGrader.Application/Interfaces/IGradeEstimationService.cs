using PokemonCardGrader.Domain.Entities;
using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Application.Interfaces;

public interface IGradeEstimationService
{
    Task<List<GradeEstimate>> EstimateAllCompaniesAsync(
        Guid cardSubmissionId,
        ConditionScores scores,
        CancellationToken ct = default);
}
