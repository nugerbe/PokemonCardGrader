using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCvSharp;
using PokemonCardGrader.Application.Configuration;
using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Infrastructure.ImageAnalysis;

/// <summary>
/// Produces debug images with visual overlays for card detection, border lines,
/// defect bounding boxes, and corner/edge analysis regions.
/// Only active when <see cref="CardAnalysisOptions.DebugEnabled"/> is true.
/// </summary>
public sealed class DebugVisualizer(
    IOptions<CardAnalysisOptions> options,
    ILogger<DebugVisualizer> logger)
{
    private readonly CardAnalysisOptions _opts = options.Value;

    /// <summary>
    /// Saves a debug image showing the detected card quadrilateral on the source image.
    /// </summary>
    public void DrawCardDetection(Mat src, Point2f[]? quad, string tag)
    {
        if (!_opts.DebugEnabled) return;

        using var debug = src.Clone();

        if (quad is not null)
        {
            var pts = quad.Select(p => new Point((int)p.X, (int)p.Y)).ToArray();
            Cv2.Polylines(debug, [pts], isClosed: true, color: new Scalar(0, 255, 0), thickness: 3);

            for (var i = 0; i < pts.Length; i++)
            {
                Cv2.Circle(debug, pts[i], 8, new Scalar(0, 0, 255), -1);
                Cv2.PutText(debug, $"P{i}", new Point(pts[i].X + 10, pts[i].Y - 10),
                    HersheyFonts.HersheySimplex, 0.7, new Scalar(255, 255, 0), 2);
            }
        }
        else
        {
            Cv2.PutText(debug, "NO CARD DETECTED", new Point(20, 40),
                HersheyFonts.HersheySimplex, 1.2, new Scalar(0, 0, 255), 3);
        }

        SaveDebugImage(debug, $"detection-{tag}");
    }

    /// <summary>
    /// Saves a debug image showing border lines on the normalized card.
    /// </summary>
    public void DrawBorderLines(Mat normalized, BorderLines borders, string tag)
    {
        if (!_opts.DebugEnabled) return;

        using var debug = normalized.Clone();
        var w = debug.Cols;
        var h = debug.Rows;

        var leftX = (int)(borders.LeftBorderX * w);
        var rightX = (int)(borders.RightBorderX * w);
        var topY = (int)(borders.TopBorderY * h);
        var bottomY = (int)(borders.BottomBorderY * h);

        Cv2.Line(debug, new Point(leftX, 0), new Point(leftX, h), new Scalar(255, 0, 0), 2);
        Cv2.Line(debug, new Point(rightX, 0), new Point(rightX, h), new Scalar(255, 0, 0), 2);
        Cv2.Line(debug, new Point(0, topY), new Point(w, topY), new Scalar(255, 0, 0), 2);
        Cv2.Line(debug, new Point(0, bottomY), new Point(w, bottomY), new Scalar(255, 0, 0), 2);

        Cv2.PutText(debug, $"L={borders.LeftBorderX:F3}", new Point(leftX + 5, 20),
            HersheyFonts.HersheySimplex, 0.5, new Scalar(0, 255, 255), 1);
        Cv2.PutText(debug, $"R={borders.RightBorderX:F3}", new Point(rightX - 80, 20),
            HersheyFonts.HersheySimplex, 0.5, new Scalar(0, 255, 255), 1);
        Cv2.PutText(debug, $"T={borders.TopBorderY:F3}", new Point(5, topY + 20),
            HersheyFonts.HersheySimplex, 0.5, new Scalar(0, 255, 255), 1);
        Cv2.PutText(debug, $"B={borders.BottomBorderY:F3}", new Point(5, bottomY - 5),
            HersheyFonts.HersheySimplex, 0.5, new Scalar(0, 255, 255), 1);

        SaveDebugImage(debug, $"borders-{tag}");
    }

    /// <summary>
    /// Saves a debug image showing detected defects as bounding boxes.
    /// </summary>
    public void DrawDefects(Mat normalized, List<DetectedDefect> defects, string tag)
    {
        if (!_opts.DebugEnabled) return;

        using var debug = normalized.Clone();
        var w = debug.Cols;
        var h = debug.Rows;

        foreach (var defect in defects)
        {
            var rect = new Rect(
                (int)(defect.X * w),
                (int)(defect.Y * h),
                (int)(defect.Width * w),
                (int)(defect.Height * h));

            var color = defect.Type switch
            {
                "scratch" => new Scalar(0, 165, 255),
                "dent" => new Scalar(0, 0, 255),
                _ => new Scalar(255, 0, 255)
            };

            Cv2.Rectangle(debug, rect, color, 2);
            Cv2.PutText(debug, $"{defect.Type} ({defect.Confidence:F2})",
                new Point(rect.X, rect.Y - 5),
                HersheyFonts.HersheySimplex, 0.4, color, 1);
        }

        Cv2.PutText(debug, $"Defects: {defects.Count}", new Point(5, 20),
            HersheyFonts.HersheySimplex, 0.6, new Scalar(0, 255, 0), 2);

        SaveDebugImage(debug, $"defects-{tag}");
    }

    /// <summary>
    /// Saves a composite debug image with all overlays (detection + borders + defects).
    /// </summary>
    public void DrawComposite(Mat src, Point2f[]? quad, Mat? normalized, BorderLines? borders,
        List<DetectedDefect>? defects, CenteringMeasurement? centering, string tag)
    {
        if (!_opts.DebugEnabled) return;

        if (normalized is not null)
        {
            using var debug = normalized.Clone();
            var w = debug.Cols;
            var h = debug.Rows;

            // Draw borders
            if (borders is not null)
            {
                var leftX = (int)(borders.LeftBorderX * w);
                var rightX = (int)(borders.RightBorderX * w);
                var topY = (int)(borders.TopBorderY * h);
                var bottomY = (int)(borders.BottomBorderY * h);

                Cv2.Line(debug, new Point(leftX, 0), new Point(leftX, h), new Scalar(255, 0, 0), 2);
                Cv2.Line(debug, new Point(rightX, 0), new Point(rightX, h), new Scalar(255, 0, 0), 2);
                Cv2.Line(debug, new Point(0, topY), new Point(w, topY), new Scalar(255, 0, 0), 2);
                Cv2.Line(debug, new Point(0, bottomY), new Point(w, bottomY), new Scalar(255, 0, 0), 2);
            }

            // Draw defects
            if (defects is not null)
            {
                foreach (var defect in defects)
                {
                    var rect = new Rect(
                        (int)(defect.X * w), (int)(defect.Y * h),
                        (int)(defect.Width * w), (int)(defect.Height * h));
                    Cv2.Rectangle(debug, rect, new Scalar(0, 0, 255), 2);
                }
            }

            // Draw centering text
            if (centering is not null)
            {
                Cv2.PutText(debug, $"LR: {centering.LeftRightFront:F1}/{100 - centering.LeftRightFront:F1}",
                    new Point(5, h - 40), HersheyFonts.HersheySimplex, 0.6, new Scalar(0, 255, 0), 2);
                Cv2.PutText(debug, $"TB: {centering.TopBottomFront:F1}/{100 - centering.TopBottomFront:F1}",
                    new Point(5, h - 15), HersheyFonts.HersheySimplex, 0.6, new Scalar(0, 255, 0), 2);
            }

            SaveDebugImage(debug, $"composite-{tag}");
        }

        // Also save source with detection overlay
        DrawCardDetection(src, quad, tag);
    }

    private void SaveDebugImage(Mat image, string filename)
    {
        try
        {
            Directory.CreateDirectory(_opts.DebugOutputPath);
            var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
            var path = Path.Combine(_opts.DebugOutputPath, $"{filename}-{timestamp}.png");
            Cv2.ImWrite(path, image);
            logger.LogDebug("Debug image saved: {Path}", path);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to save debug image: {Filename}", filename);
        }
    }
}
