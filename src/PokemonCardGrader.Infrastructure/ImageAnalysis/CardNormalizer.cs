using Microsoft.Extensions.Options;
using OpenCvSharp;
using PokemonCardGrader.Application.Configuration;

namespace PokemonCardGrader.Infrastructure.ImageAnalysis;

/// <summary>
/// Perspective-corrects a detected card quadrilateral into a standard rectangular image.
/// Output dimensions are configurable (default 630x880, matching standard card proportions).
/// </summary>
public sealed class CardNormalizer(IOptions<CardAnalysisOptions> options)
{
    private readonly CardAnalysisOptions _opts = options.Value;

    /// <summary>
    /// Standard perspective warp: maps the detected quad to the corners of the
    /// output Mat (i.e., the card fills the entire result, edge-to-edge).
    /// This is the input the analyzer expects — every centering / defect /
    /// region-segmentation routine downstream assumes the card fills the frame.
    /// The caller is responsible for disposing the returned Mat.
    /// </summary>
    public Mat Normalize(Mat src, Point2f[] quad) => Warp(src, quad, expansionFraction: 0.0);

    /// <summary>
    /// Expanded perspective warp for client display. The detected quad is
    /// mapped to a centered INSET rectangle inside the W×H output Mat, leaving
    /// a margin filled with source-image pixels from outside the detected
    /// boundary. This recovers the outer card edge in cases where detection
    /// undershot to the inner artwork frame, and gives the user a visible
    /// reference for the actual card boundary in any case.
    ///
    /// <paramref name="expansionFraction"/> is the inset on each side, expressed
    /// as a fraction of the output dimensions. 0.10 means the card occupies the
    /// central 80% of the output (10% margin on each side).
    /// </summary>
    public Mat NormalizeWithExpansion(Mat src, Point2f[] quad, double expansionFraction)
        => Warp(src, quad, expansionFraction);

    private Mat Warp(Mat src, Point2f[] quad, double expansionFraction)
    {
        var w = _opts.NormalizedWidth;
        var h = _opts.NormalizedHeight;

        var f = Math.Clamp(expansionFraction, 0.0, 0.45);
        var padX = (float)(w * f);
        var padY = (float)(h * f);

        // When f = 0 these collapse to (0,0)→(W-1,H-1), the original behaviour.
        // When f > 0 the detected quad maps to an inset rectangle and the
        // surrounding margin is filled by Cv2.WarpPerspective from source-image
        // pixels OUTSIDE the detected card boundary — exactly the breathing
        // room that makes the centering inspection work.
        var dst = new Point2f[]
        {
            new(padX,             padY),
            new(w - 1 - padX,     padY),
            new(w - 1 - padX,     h - 1 - padY),
            new(padX,             h - 1 - padY)
        };

        using var transform = Cv2.GetPerspectiveTransform(quad, dst);
        var result = new Mat();
        Cv2.WarpPerspective(
            src, result, transform, new Size(w, h),
            // Edge-replicate at the boundary so if the expansion runs off the
            // source image we extend rather than fill with black.
            borderMode: BorderTypes.Replicate);
        return result;
    }
}
