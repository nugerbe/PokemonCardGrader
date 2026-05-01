using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCvSharp;
using PokemonCardGrader.Application.Configuration;

namespace PokemonCardGrader.Infrastructure.ImageAnalysis;

/// <summary>
/// Phase 11: Refines alignment of the normalized card image.
/// After perspective correction, detects residual rotation, corrects it,
/// and ensures consistent margins via auto-cropping.
/// </summary>
public sealed class AlignmentRefiner(
    IOptions<CardAnalysisOptions> options,
    ILogger<AlignmentRefiner> logger)
{
    private readonly CardAnalysisOptions _opts = options.Value;

    /// <summary>
    /// Refines the alignment of a normalized card image.
    /// Returns a new Mat with corrected rotation and cropped margins.
    /// The caller is responsible for disposing the returned Mat.
    /// </summary>
    public Mat Refine(Mat normalized)
    {
        var residualAngle = DetectResidualRotation(normalized);
        var corrected = normalized;
        var didRotate = false;

        if (Math.Abs(residualAngle) > 0.1 && Math.Abs(residualAngle) <= _opts.MaxAlignmentRotation)
        {
            corrected = CorrectRotation(normalized, residualAngle);
            didRotate = true;
            logger.LogInformation("Alignment: corrected {Angle:F2}° residual rotation.", residualAngle);
        }
        else if (Math.Abs(residualAngle) > _opts.MaxAlignmentRotation)
        {
            logger.LogWarning(
                "Alignment: detected {Angle:F2}° residual rotation exceeds max {Max:F1}° — skipping correction.",
                residualAngle, _opts.MaxAlignmentRotation);
        }

        // Auto-crop to remove edge artifacts
        var cropped = AutoCrop(corrected);

        if (didRotate && !ReferenceEquals(corrected, normalized))
        {
            corrected.Dispose();
        }

        // Resize back to normalized dimensions for consistent downstream processing
        if (cropped.Width != _opts.NormalizedWidth || cropped.Height != _opts.NormalizedHeight)
        {
            var resized = new Mat();
            Cv2.Resize(cropped, resized, new Size(_opts.NormalizedWidth, _opts.NormalizedHeight),
                interpolation: InterpolationFlags.Lanczos4);
            cropped.Dispose();
            return resized;
        }

        return cropped;
    }

    private double DetectResidualRotation(Mat image)
    {
        using var gray = new Mat();
        if (image.Channels() > 1)
            Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
        else
            image.CopyTo(gray);

        using var edges = new Mat();
        Cv2.Canny(gray, edges, 50, 150);

        // Use HoughLinesP to detect dominant lines
        var lines = Cv2.HoughLinesP(edges, 1, Math.PI / 180, threshold: 80,
            minLineLength: image.Width / 4, maxLineGap: 10);

        if (lines.Length == 0)
        {
            logger.LogInformation("Alignment: no lines detected for rotation estimation.");
            return 0;
        }

        // Collect angles of near-horizontal and near-vertical lines
        var horizontalAngles = new List<double>();
        var verticalAngles = new List<double>();

        foreach (var line in lines)
        {
            var dx = line.P2.X - line.P1.X;
            var dy = line.P2.Y - line.P1.Y;
            var angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;

            // Near-horizontal (within 15° of 0° or 180°)
            if (Math.Abs(angle) < 15 || Math.Abs(angle - 180) < 15 || Math.Abs(angle + 180) < 15)
            {
                var normalized = angle;
                if (normalized > 90) normalized -= 180;
                if (normalized < -90) normalized += 180;
                horizontalAngles.Add(normalized);
            }
            // Near-vertical (within 15° of 90° or -90°)
            else if (Math.Abs(Math.Abs(angle) - 90) < 15)
            {
                var dev = angle > 0 ? angle - 90 : angle + 90;
                verticalAngles.Add(dev);
            }
        }

        // Weighted median of horizontal angles (more reliable on cards)
        if (horizontalAngles.Count >= 3)
        {
            horizontalAngles.Sort();
            var median = horizontalAngles[horizontalAngles.Count / 2];
            logger.LogInformation(
                "Alignment: estimated {Angle:F3}° from {Count} horizontal lines.",
                median, horizontalAngles.Count);
            return median;
        }

        if (verticalAngles.Count >= 3)
        {
            verticalAngles.Sort();
            var median = verticalAngles[verticalAngles.Count / 2];
            logger.LogInformation(
                "Alignment: estimated {Angle:F3}° from {Count} vertical lines.",
                median, verticalAngles.Count);
            return median;
        }

        return 0;
    }

    private static Mat CorrectRotation(Mat image, double angleDegrees)
    {
        var center = new Point2f(image.Width / 2f, image.Height / 2f);
        using var rotMatrix = Cv2.GetRotationMatrix2D(center, angleDegrees, 1.0);

        var result = new Mat();
        Cv2.WarpAffine(image, result, rotMatrix, image.Size(),
            InterpolationFlags.Lanczos4, BorderTypes.Reflect101);
        return result;
    }

    private Mat AutoCrop(Mat image)
    {
        var marginX = (int)(image.Width * _opts.AlignmentCropMargin);
        var marginY = (int)(image.Height * _opts.AlignmentCropMargin);

        if (marginX < 1 && marginY < 1)
        {
            var clone = new Mat();
            image.CopyTo(clone);
            return clone;
        }

        var cropRect = new Rect(
            marginX, marginY,
            image.Width - 2 * marginX,
            image.Height - 2 * marginY);

        // Clamp to valid range
        cropRect.X = Math.Max(0, cropRect.X);
        cropRect.Y = Math.Max(0, cropRect.Y);
        cropRect.Width = Math.Min(cropRect.Width, image.Width - cropRect.X);
        cropRect.Height = Math.Min(cropRect.Height, image.Height - cropRect.Y);

        if (cropRect.Width < 100 || cropRect.Height < 100)
        {
            var clone = new Mat();
            image.CopyTo(clone);
            return clone;
        }

        return new Mat(image, cropRect).Clone();
    }
}
