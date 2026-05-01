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
    /// The caller is responsible for disposing the returned Mat.
    /// </summary>
    public Mat Normalize(Mat src, Point2f[] quad)
    {
        var w = _opts.NormalizedWidth;
        var h = _opts.NormalizedHeight;

        var dst = new Point2f[]
        {
            new(0, 0),
            new(w - 1, 0),
            new(w - 1, h - 1),
            new(0, h - 1)
        };

        using var transform = Cv2.GetPerspectiveTransform(quad, dst);
        var result = new Mat();
        Cv2.WarpPerspective(src, result, transform, new Size(w, h));
        return result;
    }
}
