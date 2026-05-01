namespace PokemonCardGrader.Domain.ValueObjects;

/// <summary>
/// Defines named regions of a normalized card image for targeted analysis.
/// All coordinates are pixel-based on the normalized card dimensions.
/// </summary>
public sealed record CardRegions
{
    /// <summary>Full border region (outer edge of card).</summary>
    public required RegionRect BorderRegion { get; init; }

    /// <summary>Artwork/illustration area inside the borders.</summary>
    public required RegionRect ArtworkRegion { get; init; }

    /// <summary>Text region (name, HP, attacks, description).</summary>
    public required RegionRect TextRegion { get; init; }

    /// <summary>Four corner zones [TL, TR, BR, BL].</summary>
    public required List<RegionRect> CornerZones { get; init; }

    /// <summary>Four edge zones [Top, Right, Bottom, Left].</summary>
    public required List<RegionRect> EdgeZones { get; init; }

    /// <summary>Inner card area (excluding borders).</summary>
    public required RegionRect InnerRegion { get; init; }
}

/// <summary>
/// A rectangular region in pixel coordinates on the normalized card image.
/// </summary>
public sealed record RegionRect
{
    public required int X { get; init; }
    public required int Y { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }

    /// <summary>Label for the region (e.g., "TopLeft", "Border", "Artwork").</summary>
    public required string Label { get; init; }
}
