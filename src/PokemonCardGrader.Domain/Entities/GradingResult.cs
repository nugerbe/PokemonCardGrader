using PokemonCardGrader.Domain.Enums;

namespace PokemonCardGrader.Domain.Entities;

public sealed class GradingResult
{
    public Guid Id { get; private set; }
    public Guid CardSubmissionId { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public GradingCompany Company { get; private set; }
    public double ActualGrade { get; private set; }
    public Dictionary<string, double> ActualSubGrades { get; private set; } = new();
    public string? CertificationNumber { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }

    private GradingResult() { }

    public static GradingResult Create(
        Guid cardSubmissionId,
        string userId,
        GradingCompany company,
        double actualGrade,
        Dictionary<string, double> actualSubGrades,
        string? certificationNumber)
    {
        return new GradingResult
        {
            Id = Guid.NewGuid(),
            CardSubmissionId = cardSubmissionId,
            UserId = userId,
            Company = company,
            ActualGrade = actualGrade,
            ActualSubGrades = actualSubGrades,
            CertificationNumber = certificationNumber,
            RecordedAt = DateTimeOffset.UtcNow
        };
    }
}
