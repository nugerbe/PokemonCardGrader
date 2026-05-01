namespace PokemonCardGrader.Domain.ValueObjects;

public sealed record UserCorrection
{
    public Guid CardImageId { get; init; }
    public List<NormalizedPoint> OuterGuides { get; init; } = [];
    public List<NormalizedPoint> InnerGuides { get; init; } = [];
    public double LeftRightCenteringPercent { get; init; } = 50.0;
    public double TopBottomCenteringPercent { get; init; } = 50.0;
}
