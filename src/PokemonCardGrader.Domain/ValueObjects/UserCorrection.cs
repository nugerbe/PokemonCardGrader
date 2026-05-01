namespace PokemonCardGrader.Domain.ValueObjects;

/// <summary>
/// Represents a user's correction to the automated analysis overlay.
/// Used to recalculate scores based on adjusted detection results.
/// </summary>
public sealed record UserCorrection
{
    public Guid CardImageId { get; init; }
    public List<NormalizedPoint>? AdjustedBoundary { get; init; }
    public BorderLines? AdjustedBorders { get; init; }
    public List<int> DismissedDefectIndices { get; init; } = [];
}
