using OpenCvSharp;

namespace PokemonCardGrader.Infrastructure.Tests.ImageAnalysis;

/// <summary>
/// Builds synthetic card-on-background scene images with precisely placed border lines.
/// Each image has a known centering ratio for integration-testing the analysis pipeline.
/// </summary>
internal static class TestCardImageGenerator
{
    // Scene dimensions
    private const int SceneWidth = 800;
    private const int SceneHeight = 1120;

    // Card rect within the scene
    private const int CardX = 100;
    private const int CardY = 141;
    private const int CardWidth = 600;
    private const int CardHeight = 838; // aspect ratio ~0.716 matching 63/88

    // Normalized card space used by the analysis service
    private const double NormalizedCardWidth = 500.0;
    private const double NormalizedCardHeight = 700.0;

    /// <summary>
    /// Creates a synthetic card image with the specified border widths.
    /// Border pixel inputs are in normalized-card space (500x700).
    /// Valid ranges: left/right 10-80, top/bottom 14-112.
    /// </summary>
    public static (MemoryStream ImageStream, ExpectedCentering Expected) CreateCardImage(
        int leftBorderPx, int rightBorderPx, int topBorderPx, int bottomBorderPx)
    {
        using var scene = new Mat(SceneHeight, SceneWidth, MatType.CV_8UC3);

        // Fill background with light gray
        scene.SetTo(new Scalar(200, 200, 200));

        // Draw card rectangle filled with bright yellow border color
        // BGR (0, 200, 220) -> grayscale ~180
        var cardRect = new Rect(CardX, CardY, CardWidth, CardHeight);
        scene[cardRect].SetTo(new Scalar(0, 200, 220));

        // Compute artwork inset in scene coordinates
        // Map from normalized-card space to scene-card space
        var artLeftScene = (int)(leftBorderPx / NormalizedCardWidth * CardWidth);
        var artRightScene = (int)(rightBorderPx / NormalizedCardWidth * CardWidth);
        var artTopScene = (int)(topBorderPx / NormalizedCardHeight * CardHeight);
        var artBottomScene = (int)(bottomBorderPx / NormalizedCardHeight * CardHeight);

        var artX = CardX + artLeftScene;
        var artY = CardY + artTopScene;
        var artW = CardWidth - artLeftScene - artRightScene;
        var artH = CardHeight - artTopScene - artBottomScene;

        if (artW > 0 && artH > 0)
        {
            // Fill artwork with dark blue - BGR (100, 30, 20) -> grayscale ~40
            var artRect = new Rect(artX, artY, artW, artH);
            scene[artRect].SetTo(new Scalar(100, 30, 20));
        }

        // Draw 3px black outline on card edges for strong detection
        Cv2.Rectangle(scene, cardRect, new Scalar(0, 0, 0), thickness: 3);

        // Encode to PNG
        Cv2.ImEncode(".png", scene, out var buf);
        var ms = new MemoryStream(buf);

        // Compute expected centering values
        var lr = leftBorderPx / (double)(leftBorderPx + rightBorderPx) * 100.0;
        var tb = topBorderPx / (double)(topBorderPx + bottomBorderPx) * 100.0;

        var expected = new ExpectedCentering
        {
            LeftRightPercent = lr,
            TopBottomPercent = tb,
            LeftBorderX = leftBorderPx / NormalizedCardWidth,
            RightBorderX = 1.0 - rightBorderPx / NormalizedCardWidth,
            TopBorderY = topBorderPx / NormalizedCardHeight,
            BottomBorderY = 1.0 - bottomBorderPx / NormalizedCardHeight,
        };

        return (ms, expected);
    }
}

internal sealed record ExpectedCentering
{
    /// <summary>Left/right centering percentage (0-100). 50 = perfectly centered.</summary>
    public required double LeftRightPercent { get; init; }

    /// <summary>Top/bottom centering percentage (0-100). 50 = perfectly centered.</summary>
    public required double TopBottomPercent { get; init; }

    /// <summary>Left border position as fraction of card width (0-1).</summary>
    public required double LeftBorderX { get; init; }

    /// <summary>Right border position as fraction of card width (0-1).</summary>
    public required double RightBorderX { get; init; }

    /// <summary>Top border position as fraction of card height (0-1).</summary>
    public required double TopBorderY { get; init; }

    /// <summary>Bottom border position as fraction of card height (0-1).</summary>
    public required double BottomBorderY { get; init; }
}
