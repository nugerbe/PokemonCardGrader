using PokemonCardGrader.Domain.Enums;

namespace PokemonCardGrader.Application.Interfaces;

public interface IMlTrainingService
{
    Task<bool> TrainModelAsync(GradingCompany company, CancellationToken ct = default);
    bool ModelExists(GradingCompany company);
}
