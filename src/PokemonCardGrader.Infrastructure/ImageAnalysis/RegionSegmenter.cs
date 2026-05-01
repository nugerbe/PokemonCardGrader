using Microsoft.Extensions.Options;
using OpenCvSharp;
using PokemonCardGrader.Application.Configuration;
using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Infrastructure.ImageAnalysis;

/// <summary>
/// Phase 12: Segments a normalized card image into named regions:
/// border, artwork, text, 4 corner zones, and 4 edge zones.
/// All regions are defined in pixel coordinates on the normalized image.
/// </summary>
public sealed class RegionSegmenter(IOptions<CardAnalysisOptions> options)
{
    private readonly CardAnalysisOptions _opts = options.Value;

    /// <summary>
    /// Computes card regions based on normalized card dimensions.
    /// </summary>
    public CardRegions Segment(int width, int height)
    {
        var borderW = (int)(width * _opts.SegmentBorderFraction);
        var borderH = (int)(height * _opts.SegmentBorderFraction);

        // Border region (the full outer band)
        var borderRegion = new RegionRect
        {
            X = 0, Y = 0,
            Width = width, Height = height,
            Label = "Border"
        };

        // Inner region (everything inside the border)
        var innerRegion = new RegionRect
        {
            X = borderW, Y = borderH,
            Width = width - 2 * borderW,
            Height = height - 2 * borderH,
            Label = "Inner"
        };

        // Artwork region
        var artTop = (int)(height * _opts.ArtworkTopFraction);
        var artHeight = (int)(height * _opts.ArtworkHeightFraction);
        var artworkRegion = new RegionRect
        {
            X = borderW, Y = artTop,
            Width = width - 2 * borderW,
            Height = Math.Min(artHeight, height - artTop - borderH),
            Label = "Artwork"
        };

        // Text region
        var textTop = (int)(height * _opts.TextTopFraction);
        var textHeight = (int)(height * _opts.TextHeightFraction);
        var textRegion = new RegionRect
        {
            X = borderW, Y = textTop,
            Width = width - 2 * borderW,
            Height = Math.Min(textHeight, height - textTop - borderH),
            Label = "Text"
        };

        // Corner zones
        var cornerSize = (int)(width * _opts.SegmentCornerFraction);
        var cornerZones = new List<RegionRect>
        {
            new() { X = 0, Y = 0, Width = cornerSize, Height = cornerSize, Label = "TopLeft" },
            new() { X = width - cornerSize, Y = 0, Width = cornerSize, Height = cornerSize, Label = "TopRight" },
            new() { X = width - cornerSize, Y = height - cornerSize, Width = cornerSize, Height = cornerSize, Label = "BottomRight" },
            new() { X = 0, Y = height - cornerSize, Width = cornerSize, Height = cornerSize, Label = "BottomLeft" }
        };

        // Edge zones (strips along each side, excluding corners)
        var edgeWidth = (int)(width * _opts.SegmentEdgeFraction);
        var edgeZones = new List<RegionRect>
        {
            new() { X = cornerSize, Y = 0, Width = width - 2 * cornerSize, Height = edgeWidth, Label = "TopEdge" },
            new() { X = width - edgeWidth, Y = cornerSize, Width = edgeWidth, Height = height - 2 * cornerSize, Label = "RightEdge" },
            new() { X = cornerSize, Y = height - edgeWidth, Width = width - 2 * cornerSize, Height = edgeWidth, Label = "BottomEdge" },
            new() { X = 0, Y = cornerSize, Width = edgeWidth, Height = height - 2 * cornerSize, Label = "LeftEdge" }
        };

        return new CardRegions
        {
            BorderRegion = borderRegion,
            ArtworkRegion = artworkRegion,
            TextRegion = textRegion,
            CornerZones = cornerZones,
            EdgeZones = edgeZones,
            InnerRegion = innerRegion
        };
    }

    /// <summary>
    /// Extracts a Mat sub-region from the source image.
    /// Returns a new Mat that the caller must dispose.
    /// </summary>
    public static Mat ExtractRegion(Mat source, RegionRect region)
    {
        var rect = new Rect(
            Math.Clamp(region.X, 0, source.Width - 1),
            Math.Clamp(region.Y, 0, source.Height - 1),
            Math.Min(region.Width, source.Width - Math.Clamp(region.X, 0, source.Width - 1)),
            Math.Min(region.Height, source.Height - Math.Clamp(region.Y, 0, source.Height - 1)));

        if (rect.Width <= 0 || rect.Height <= 0)
            return new Mat(1, 1, source.Type(), Scalar.Black);

        return new Mat(source, rect).Clone();
    }

    /// <summary>
    /// Determines which region label a point at (x, y) falls into (normalized 0-1 coordinates).
    /// </summary>
    public string ClassifyPoint(double x, double y, int width, int height)
    {
        var px = (int)(x * width);
        var py = (int)(y * height);
        var regions = Segment(width, height);

        foreach (var corner in regions.CornerZones)
        {
            if (px >= corner.X && px < corner.X + corner.Width &&
                py >= corner.Y && py < corner.Y + corner.Height)
                return corner.Label;
        }

        foreach (var edge in regions.EdgeZones)
        {
            if (px >= edge.X && px < edge.X + edge.Width &&
                py >= edge.Y && py < edge.Y + edge.Height)
                return edge.Label;
        }

        if (px >= regions.ArtworkRegion.X && px < regions.ArtworkRegion.X + regions.ArtworkRegion.Width &&
            py >= regions.ArtworkRegion.Y && py < regions.ArtworkRegion.Y + regions.ArtworkRegion.Height)
            return "Artwork";

        if (px >= regions.TextRegion.X && px < regions.TextRegion.X + regions.TextRegion.Width &&
            py >= regions.TextRegion.Y && py < regions.TextRegion.Y + regions.TextRegion.Height)
            return "Text";

        return "Border";
    }
}
