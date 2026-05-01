using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Application.DTOs;

public sealed record CardSubmissionDto
{
    public required Guid Id { get; init; }
    public required string CardName { get; init; }
    public required string SetName { get; init; }
    public required string Number { get; init; }
    public string? CardImageUrl { get; init; }
    public ConditionScores? ManualScores { get; init; }
    public ConditionScores? ImageDerivedScores { get; init; }
    public ConditionScores? FinalScores { get; init; }
    public required List<GradeEstimateDto> Estimates { get; init; }
    public required List<CardImageDto> Images { get; init; }
    public GradingResultDto? ActualResult { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}
