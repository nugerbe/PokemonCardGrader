namespace PokemonCardGrader.Application.DTOs;

public sealed record DashboardDto
{
    public required int TotalSubmissions { get; init; }
    public required int GradedCards { get; init; }
    public required Dictionary<string, int> GradeDistribution { get; init; }
    public required List<CardSubmissionDto> RecentSubmissions { get; init; }
    public required double? AverageEstimatedGrade { get; init; }
}
