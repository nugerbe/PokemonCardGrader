using PokemonCardGrader.Domain.Enums;

namespace PokemonCardGrader.Application.DTOs;

public sealed record GradeEstimateDto
{
    public required GradingCompany Company { get; init; }
    public required double PredictedGrade { get; init; }
    public required Dictionary<string, double> SubGrades { get; init; }
    public required double Confidence { get; init; }
    public required bool IsRuleBased { get; init; }
    public required string Label { get; init; }
}
