using PokemonCardGrader.Domain.Enums;

namespace PokemonCardGrader.Domain.Entities;

public sealed class GradeEstimate
{
    public Guid Id { get; private set; }
    public Guid CardSubmissionId { get; private set; }
    public GradingCompany Company { get; private set; }
    public double PredictedGrade { get; private set; }
    public Dictionary<string, double> SubGrades { get; private set; } = new();
    public double Confidence { get; private set; }
    public bool IsRuleBased { get; private set; }
    public string Label { get; private set; } = string.Empty;
    public DateTimeOffset EstimatedAt { get; private set; }

    private GradeEstimate() { }

    public static GradeEstimate Create(
        Guid cardSubmissionId,
        GradingCompany company,
        double predictedGrade,
        Dictionary<string, double> subGrades,
        double confidence,
        bool isRuleBased,
        string label)
    {
        return new GradeEstimate
        {
            Id = Guid.NewGuid(),
            CardSubmissionId = cardSubmissionId,
            Company = company,
            PredictedGrade = predictedGrade,
            SubGrades = subGrades,
            Confidence = confidence,
            IsRuleBased = isRuleBased,
            Label = label,
            EstimatedAt = DateTimeOffset.UtcNow
        };
    }
}
