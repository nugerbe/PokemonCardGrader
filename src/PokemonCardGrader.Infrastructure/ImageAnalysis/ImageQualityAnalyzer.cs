using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCvSharp;
using PokemonCardGrader.Application.Configuration;
using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Infrastructure.ImageAnalysis;

/// <summary>
/// Pre-processing gate that evaluates input image quality before the main analysis pipeline.
/// Checks blur, exposure, glare, resolution, and noise levels.
/// Returns a quality assessment with score, issues, and a recommended action.
/// </summary>
public sealed class ImageQualityAnalyzer(
    IOptions<CardAnalysisOptions> options,
    ILogger<ImageQualityAnalyzer> logger)
{
    private readonly CardAnalysisOptions _opts = options.Value;

    public ImageQualityAssessment Assess(Mat image)
    {
        var issues = new List<string>();

        var sharpnessScore = MeasureSharpness(image, issues);
        var exposureScore = MeasureExposure(image, issues);
        var glareScore = MeasureGlare(image, issues);
        var resolutionScore = MeasureResolution(image, issues);
        var noiseScore = MeasureNoise(image, issues);

        // Weighted overall — sharpness and exposure matter most
        var overall = (sharpnessScore * 0.30)
                    + (exposureScore * 0.25)
                    + (glareScore * 0.20)
                    + (resolutionScore * 0.15)
                    + (noiseScore * 0.10);

        var passed = overall >= _opts.QualityGateThreshold;
        var action = overall >= _opts.QualityReduceConfidenceThreshold
            ? "proceed"
            : overall >= _opts.QualityGateThreshold
                ? "reduce_confidence"
                : "reject";

        logger.LogInformation(
            "Image quality: overall={Overall:F3} sharpness={Sharpness:F3} exposure={Exposure:F3} " +
            "glare={Glare:F3} resolution={Resolution:F3} noise={Noise:F3} action={Action} issues=[{Issues}]",
            overall, sharpnessScore, exposureScore, glareScore, resolutionScore, noiseScore,
            action, string.Join("; ", issues));

        return new ImageQualityAssessment
        {
            OverallScore = Math.Round(overall, 4),
            SharpnessScore = Math.Round(sharpnessScore, 4),
            ExposureScore = Math.Round(exposureScore, 4),
            GlareScore = Math.Round(glareScore, 4),
            ResolutionScore = Math.Round(resolutionScore, 4),
            NoiseScore = Math.Round(noiseScore, 4),
            PassedGate = passed,
            Issues = issues,
            RecommendedAction = action
        };
    }

    private double MeasureSharpness(Mat image, List<string> issues)
    {
        using var gray = new Mat();
        if (image.Channels() > 1)
            Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
        else
            image.CopyTo(gray);

        using var laplacian = new Mat();
        Cv2.Laplacian(gray, laplacian, MatType.CV_64F);

        Cv2.MeanStdDev(laplacian, out _, out var stddev);
        var variance = stddev.Val0 * stddev.Val0;

        // Map variance to 0-1 score
        // < 10 = very blurry, 10-50 = somewhat blurry, 50-200 = acceptable, > 200 = sharp
        var score = variance switch
        {
            < 10 => 0.1,
            < 50 => 0.1 + 0.4 * ((variance - 10) / 40.0),
            < 200 => 0.5 + 0.4 * ((variance - 50) / 150.0),
            _ => 0.9 + 0.1 * Math.Min(1.0, (variance - 200) / 300.0)
        };

        if (variance < _opts.MinSharpnessForQuality)
            issues.Add($"Image is blurry (Laplacian variance={variance:F1}, min={_opts.MinSharpnessForQuality:F0})");

        return Math.Clamp(score, 0, 1);
    }

    private double MeasureExposure(Mat image, List<string> issues)
    {
        using var gray = new Mat();
        if (image.Channels() > 1)
            Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
        else
            image.CopyTo(gray);

        var mean = Cv2.Mean(gray).Val0;

        // Compute histogram spread
        Cv2.MeanStdDev(gray, out _, out var stddev);
        var contrast = stddev.Val0;

        // Analyze histogram for under/over-exposure
        using var hist = new Mat();
        var hdims = new[] { 256 };
        var ranges = new[] { new Rangef(0, 256) };
        Cv2.CalcHist([gray], [0], null, hist, 1, hdims, ranges);

        var totalPixels = gray.Rows * gray.Cols;
        var darkPixels = 0f;
        var brightPixels = 0f;
        for (var i = 0; i < 20; i++)
            darkPixels += hist.At<float>(i);
        for (var i = 236; i < 256; i++)
            brightPixels += hist.At<float>(i);

        var darkFraction = darkPixels / totalPixels;
        var brightFraction = brightPixels / totalPixels;

        // Score based on mean brightness and contrast
        double exposureScore = 1.0;

        if (mean < _opts.IdealBrightnessMin)
        {
            exposureScore -= (1.0 - mean / _opts.IdealBrightnessMin) * 0.6;
            issues.Add($"Image is underexposed (mean brightness={mean:F0})");
        }
        else if (mean > _opts.IdealBrightnessMax)
        {
            exposureScore -= ((mean - _opts.IdealBrightnessMax) / (255 - _opts.IdealBrightnessMax)) * 0.6;
            issues.Add($"Image is overexposed (mean brightness={mean:F0})");
        }

        // Penalize low contrast
        if (contrast < 30)
        {
            exposureScore -= 0.2;
            issues.Add($"Low contrast (stddev={contrast:F1})");
        }

        // Penalize heavy dark/bright clipping
        if (darkFraction > 0.25)
        {
            exposureScore -= darkFraction * 0.3;
            issues.Add($"Significant shadow clipping ({darkFraction:P0})");
        }
        if (brightFraction > 0.25)
        {
            exposureScore -= brightFraction * 0.3;
            issues.Add($"Significant highlight clipping ({brightFraction:P0})");
        }

        return Math.Clamp(exposureScore, 0, 1);
    }

    private double MeasureGlare(Mat image, List<string> issues)
    {
        using var gray = new Mat();
        if (image.Channels() > 1)
            Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
        else
            image.CopyTo(gray);

        // Count saturated pixels (near white)
        using var saturated = new Mat();
        Cv2.Threshold(gray, saturated, _opts.GlareSaturationThreshold, 255, ThresholdTypes.Binary);

        var saturatedPixels = Cv2.CountNonZero(saturated);
        var totalPixels = gray.Rows * gray.Cols;
        var glareFraction = (double)saturatedPixels / totalPixels;

        // Check for localized glare (large contiguous bright regions)
        Cv2.FindContours(saturated, out var contours, out _, RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);

        var largestGlareArea = contours.Length > 0
            ? contours.Max(c => Cv2.ContourArea(c))
            : 0;
        var largestGlareFraction = largestGlareArea / totalPixels;

        double score = 1.0;
        if (glareFraction > _opts.MaxGlareFraction)
        {
            score -= (glareFraction - _opts.MaxGlareFraction) / (1.0 - _opts.MaxGlareFraction) * 0.7;
            issues.Add($"Excessive glare ({glareFraction:P1} saturated pixels)");
        }
        else if (glareFraction > _opts.MaxGlareFraction * 0.5)
        {
            score -= 0.15;
        }

        // Large contiguous glare is worse than scattered bright pixels
        if (largestGlareFraction > 0.03)
        {
            score -= largestGlareFraction * 2.0;
            issues.Add($"Large glare spot detected ({largestGlareFraction:P1} of image)");
        }

        return Math.Clamp(score, 0, 1);
    }

    private double MeasureResolution(Mat image, List<string> issues)
    {
        var minDim = Math.Min(image.Width, image.Height);

        if (minDim < _opts.MinImageDimension)
        {
            issues.Add($"Image too small ({image.Width}x{image.Height}, min={_opts.MinImageDimension}px)");
            return Math.Max(0.1, (double)minDim / _opts.MinImageDimension * 0.3);
        }

        if (minDim < _opts.IdealImageDimension)
        {
            var score = 0.5 + 0.5 * ((double)(minDim - _opts.MinImageDimension) /
                                       (_opts.IdealImageDimension - _opts.MinImageDimension));
            issues.Add($"Image resolution is adequate but not ideal ({image.Width}x{image.Height})");
            return Math.Clamp(score, 0.3, 0.95);
        }

        return 1.0;
    }

    private double MeasureNoise(Mat image, List<string> issues)
    {
        using var gray = new Mat();
        if (image.Channels() > 1)
            Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
        else
            image.CopyTo(gray);

        // Estimate noise using median absolute deviation of Laplacian
        // (robust noise estimator: sigma = MAD(Laplacian) * 1.4826 / sqrt(2))
        using var laplacian = new Mat();
        Cv2.Laplacian(gray, laplacian, MatType.CV_64F, ksize: 3);

        // Convert to abs values and compute median
        using var absLap = new Mat();
        Cv2.ConvertScaleAbs(laplacian, absLap);

        // Use histogram to approximate median efficiently
        using var hist = new Mat();
        var hdims = new[] { 256 };
        var ranges = new[] { new Rangef(0, 256) };
        Cv2.CalcHist([absLap], [0], null, hist, 1, hdims, ranges);

        var totalPixels = gray.Rows * gray.Cols;
        var halfTotal = totalPixels / 2.0f;
        var cumulative = 0f;
        var median = 0;
        for (var i = 0; i < 256; i++)
        {
            cumulative += hist.At<float>(i);
            if (cumulative >= halfTotal)
            {
                median = i;
                break;
            }
        }

        // Estimate noise sigma
        var noiseEstimate = median * 1.4826 / Math.Sqrt(2);

        double score;
        if (noiseEstimate < 5)
            score = 1.0;
        else if (noiseEstimate < _opts.MaxNoiseLevel)
            score = 1.0 - 0.5 * (noiseEstimate - 5) / (_opts.MaxNoiseLevel - 5);
        else
        {
            score = 0.5 - 0.4 * Math.Min(1.0, (noiseEstimate - _opts.MaxNoiseLevel) / _opts.MaxNoiseLevel);
            issues.Add($"High noise level (estimated sigma={noiseEstimate:F1})");
        }

        return Math.Clamp(score, 0.1, 1.0);
    }
}
