using PokemonCardGrader.Domain.Enums;
using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Application.Interfaces;

public interface IMlGradePredictor
{
    /// <summary>
    /// Returns predicted grade or null if no model is available for the company.
    /// </summary>
    float? Predict(GradingCompany company, ConditionScores scores);
}
