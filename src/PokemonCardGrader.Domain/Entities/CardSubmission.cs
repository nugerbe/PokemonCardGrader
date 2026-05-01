using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Domain.Entities;

public sealed class CardSubmission
{
    public Guid Id { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public Guid PokemonCardId { get; private set; }
    public PokemonCard PokemonCard { get; private set; } = null!;
    public ConditionScores? ManualScores { get; private set; }
    public ConditionScores? ImageDerivedScores { get; private set; }
    public ConditionScores? FinalScores { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private readonly List<CardImage> _images = [];
    public IReadOnlyList<CardImage> Images => _images.AsReadOnly();

    private readonly List<GradeEstimate> _estimates = [];
    public IReadOnlyList<GradeEstimate> Estimates => _estimates.AsReadOnly();

    public GradingResult? ActualResult { get; private set; }

    private CardSubmission() { }

    public static CardSubmission Create(string userId, Guid pokemonCardId, string? notes = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new CardSubmission
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PokemonCardId = pokemonCardId,
            Notes = notes,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void SetManualScores(ConditionScores scores)
    {
        ManualScores = scores;
        UpdateFinalScores();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetImageDerivedScores(ConditionScores scores)
    {
        ImageDerivedScores = scores;
        UpdateFinalScores();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetFinalScores(ConditionScores scores)
    {
        FinalScores = scores;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void AddImage(CardImage image)
    {
        _images.Add(image);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetEstimates(IEnumerable<GradeEstimate> estimates)
    {
        _estimates.Clear();
        _estimates.AddRange(estimates);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordActualResult(GradingResult result)
    {
        ActualResult = result;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private void UpdateFinalScores()
    {
        // Prefer manual scores; use image-derived as fallback.
        // IMPORTANT: Must create a new instance — EF Core tracks owned entities
        // by reference, so assigning the same object to two owned navigations
        // (e.g. FinalScores = ImageDerivedScores) causes tracking conflicts.
        var source = ManualScores ?? ImageDerivedScores;
        FinalScores = source is not null ? CopyScores(source) : null;
    }

    private static ConditionScores CopyScores(ConditionScores source) => new()
    {
        Centering = new CenteringMeasurement
        {
            LeftRightFront = source.Centering.LeftRightFront,
            LeftRightBack = source.Centering.LeftRightBack,
            TopBottomFront = source.Centering.TopBottomFront,
            TopBottomBack = source.Centering.TopBottomBack
        },
        Corners = source.Corners,
        Edges = source.Edges,
        Surface = source.Surface
    };
}
