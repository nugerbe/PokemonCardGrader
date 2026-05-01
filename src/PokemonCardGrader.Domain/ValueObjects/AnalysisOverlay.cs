using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Domain.ValueObjects;

public sealed record AnalysisOverlay
{
    public List<NormalizedPoint> OuterGuides { get; init; } = [];
    public List<NormalizedPoint> InnerGuides { get; init; } = [];
    public double LeftRightCenteringPercent { get; init; } = 50.0;
    public double TopBottomCenteringPercent { get; init; } = 50.0;
}

/// <summary>
/// A point with coordinates normalized to 0-1 range.
/// </summary>
public sealed record NormalizedPoint
{
    public double X { get; init; }
    public double Y { get; init; }
}

/// <summary>
/// Inner border line positions normalized to 0-1 relative to the card dimensions.
/// These represent where the artwork border meets the card border on each side.
/// </summary>
public sealed record BorderLines
{
    public double LeftBorderX { get; init; }
    public double RightBorderX { get; init; }
    public double TopBorderY { get; init; }
    public double BottomBorderY { get; init; }
}
