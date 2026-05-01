using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Domain.Entities;

/// <summary>
/// Stores a user's correction to an automated analysis, serving as training data
/// for future detection improvements.
/// </summary>
public sealed class AnalysisCorrection
{
    public Guid Id { get; private set; }
    public Guid CardImageId { get; private set; }
    public Guid CardSubmissionId { get; private set; }
    public AnalysisOverlay OriginalOverlay { get; private set; } = null!;
    public ConditionScores OriginalScores { get; private set; } = null!;
    public UserCorrection Correction { get; private set; } = null!;
    public ConditionScores CorrectedScores { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }

    private AnalysisCorrection() { }

    public static AnalysisCorrection Create(
        Guid cardImageId,
        Guid cardSubmissionId,
        AnalysisOverlay originalOverlay,
        ConditionScores originalScores,
        UserCorrection correction,
        ConditionScores correctedScores)
    {
        return new AnalysisCorrection
        {
            Id = Guid.NewGuid(),
            CardImageId = cardImageId,
            CardSubmissionId = cardSubmissionId,
            OriginalOverlay = originalOverlay,
            OriginalScores = originalScores,
            Correction = correction,
            CorrectedScores = correctedScores,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
