using PokemonCardGrader.Domain.Enums;

namespace PokemonCardGrader.Application.DTOs;

public sealed record GradingResultDto
{
    public required GradingCompany Company { get; init; }
    public required double ActualGrade { get; init; }
    public required Dictionary<string, double> ActualSubGrades { get; init; }
    public string? CertificationNumber { get; init; }
    public required DateTimeOffset RecordedAt { get; init; }
}
