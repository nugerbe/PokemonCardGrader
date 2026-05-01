namespace PokemonCardGrader.Domain.ValueObjects;

/// <summary>
/// Normalized overlay data from image analysis — card boundary and border line positions.
/// All coordinates are 0-1 normalized relative to the original image dimensions.
/// </summary>
public sealed record AnalysisOverlay
{
    /// <summary>
    /// Detected card boundary corners (4 points), normalized 0-1 relative to source image.
    /// Order: top-left, top-right, bottom-right, bottom-left.
    /// </summary>
    public required List<NormalizedPoint> CardBoundary { get; init; }

    /// <summary>
    /// Detected inner border line positions, normalized 0-1 relative to the card region.
    /// </summary>
    public required BorderLines BorderLines { get; init; }
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
