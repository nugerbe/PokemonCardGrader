using PokemonCardGrader.Domain.Enums;
using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Domain.GradingEngines;

public interface IGradingRuleEngine
{
    GradingCompany Company { get; }
    GradeResult EstimateGrade(ConditionScores scores);
}
