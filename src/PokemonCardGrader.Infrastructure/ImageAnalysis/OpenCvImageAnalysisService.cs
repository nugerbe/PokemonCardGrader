using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCvSharp;
using PokemonCardGrader.Application.Configuration;
using PokemonCardGrader.Application.DTOs;
using PokemonCardGrader.Application.Interfaces;
using PokemonCardGrader.Domain.Enums;
using PokemonCardGrader.Domain.ValueObjects;
using PokemonCardGrader.Infrastructure.ML;

namespace PokemonCardGrader.Infrastructure.ImageAnalysis;

/// <summary>
/// Orchestrates the card analysis pipeline by delegating to focused components:
/// quality gate -> detection -> failure check -> normalization -> alignment ->
/// centering -> regions -> condition -> advanced defects -> features ->
/// ML inference -> hybrid combine -> confidence -> overlay -> debug.
/// Integrates debug visualization and training-data logging when enabled.
/// </summary>
public sealed class OpenCvImageAnalysisService(
    IOptions<CardAnalysisOptions> options,
    CardDetector detector,
    CardNormalizer normalizer,
    CenteringAnalyzer centeringAnalyzer,
    ConditionAnalyzer conditionAnalyzer,
    OnnxInferenceService onnxInference,
    ConfidenceScorer confidenceScorer,
    DebugVisualizer debugViz,
    AnalysisDataLogger dataLogger,
    IBorderPredictionService borderPrediction,
    ImageQualityAnalyzer qualityAnalyzer,
    FailureDetector failureDetector,
    AlignmentRefiner alignmentRefiner,
    RegionSegmenter regionSegmenter,
    AdvancedDefectAnalyzer advancedDefectAnalyzer,
    FeatureExtractor featureExtractor,
    HybridScoreCombiner hybridCombiner,
    ConfidenceCalibrator confidenceCalibrator,
    ILogger<OpenCvImageAnalysisService> logger) : IImageAnalysisService
{
    private readonly CardAnalysisOptions _opts = options.Value;

    public async Task<ImageAnalysisOutcome> AnalyzeImageAsync(
        Stream imageStream, ImageType imageType = ImageType.Front, CancellationToken ct = default)
    {
        var imageBytes = await ReadStreamAsync(imageStream, ct);
        var submissionId = Guid.NewGuid();

        logger.LogInformation(
            "Image data: {ByteCount} bytes read from stream. ImageType={ImageType}",
            imageBytes.Length, imageType);

        using var src = Mat.FromImageData(imageBytes, ImreadModes.Color);
        if (src.Empty())
        {
            logger.LogWarning("Failed to decode image from stream ({ByteCount} bytes).", imageBytes.Length);
            return new(CreateEmptyResult());
        }

        logger.LogInformation("Image decoded: {Width}x{Height} pixels.", src.Width, src.Height);

        // -- Phase 10: Image Quality Gate --
        var qualityAssessment = qualityAnalyzer.Assess(src);
        if (!qualityAssessment.PassedGate)
        {
            logger.LogWarning(
                "Image quality gate failed (score={Score:F2}): {Action}",
                qualityAssessment.OverallScore, qualityAssessment.RecommendedAction);

            return new(CreateEmptyResult() with
            {
                QualityAssessment = qualityAssessment,
                ImageQualityScore = qualityAssessment.OverallScore
            });
        }

        // -- Step 1: Detect the card quadrilateral --
        var detection = detector.DetectWithMetadata(src);
        var cardQuad = detection?.Quad;
        debugViz.DrawCardDetection(src, cardQuad, imageType.ToString().ToLowerInvariant());

        // -- Phase 21: Failure detection --
        var failureResult = failureDetector.Detect(src, cardQuad, detection?.UsedFullFrameFallback ?? false);

        if (failureResult.HasBlockingFailure)
        {
            logger.LogWarning("Blocking failure: {Type} -- {Description}",
                failureResult.FailureType, failureResult.Description);

            return new(CreateEmptyResult() with
            {
                QualityAssessment = qualityAssessment,
                FailureDetection = failureResult,
                ImageQualityScore = qualityAssessment.OverallScore
            });
        }

        if (detection is null || cardQuad is null)
        {
            logger.LogWarning("Could not detect card quadrilateral in {Width}x{Height} image.", src.Width, src.Height);
            return new(CreateEmptyResult() with
            {
                QualityAssessment = qualityAssessment,
                ImageQualityScore = qualityAssessment.OverallScore
            });
        }

        // -- Step 2: Perspective-correct to normalized rectangle --
        using var rawNormalized = normalizer.Normalize(src, cardQuad);

        // -- Phase 11: Alignment refinement --
        using var normalized = alignmentRefiner.Refine(rawNormalized);

        // -- Phase 12: Region segmentation --
        var regions = regionSegmenter.Segment(normalized.Width, normalized.Height);

        // -- Step 3: Measure centering with border prior --
        BorderPrior? prior = null;
        try { prior = await borderPrediction.GetBorderPriorAsync(ct); }
        catch (Exception ex) { logger.LogWarning(ex, "Failed to load border prior; proceeding without it."); }

        var centeringResult = centeringAnalyzer.Measure(normalized, imageType, prior);
        debugViz.DrawBorderLines(normalized, centeringResult.DetectedBorders ?? new BorderLines
        {
            LeftBorderX = 0.05,
            RightBorderX = 0.95,
            TopBorderY = 0.05,
            BottomBorderY = 0.95
        }, imageType.ToString().ToLowerInvariant());

        // -- Step 4: Analyze condition (surface, corners, edges, defects) via CV --
        var condition = conditionAnalyzer.Analyze(normalized);
        debugViz.DrawDefects(normalized, condition.Defects, imageType.ToString().ToLowerInvariant());

        // -- Phase 13: Advanced defect analysis --
        var advancedDefects = advancedDefectAnalyzer.Analyze(normalized, regions);

        // Merge advanced defects with standard CV defects
        var allCvDefects = new List<DetectedDefect>(condition.Defects);
        allCvDefects.AddRange(advancedDefects.AdvancedDefects);

        // -- Phase 14: Feature extraction --
        var features = featureExtractor.Extract(normalized, regions, centeringResult.Centering);

        // -- Step 5: Run ML inference (defect detection + surface grading) --
        var mlResult = await onnxInference.RunAsync(normalized, ct);

        // -- Phase 15: Hybrid ML/CV combination --
        var hybridResult = await hybridCombiner.CombineAsync(
            condition.CornersScore, condition.EdgesScore, condition.SurfaceScore,
            allCvDefects, features, mlResult, ct);

        // -- Step 6: Compute pipeline confidence --
        var confidenceInput = new ConfidenceScorer.ConfidenceInput(
            NormalizedImage: normalized,
            CardDetected: true,
            UsedFullFrameFallback: detection.UsedFullFrameFallback,
            DetectedBorders: centeringResult.DetectedBorders,
            CvDefects: allCvDefects,
            MlDefects: mlResult.Defects,
            CvSurfaceScore: condition.SurfaceScore,
            MlSurfaceScore: mlResult.SurfaceScore,
            DefectModelAvailable: mlResult.DefectModelAvailable,
            SurfaceModelAvailable: mlResult.SurfaceModelAvailable);

        var confidence = confidenceScorer.Score(confidenceInput);

        // -- Phase 20: Confidence calibration --
        var calibratedConfidence = confidenceCalibrator.Calibrate(confidence.Overall);

        // Adjust quality-reduced confidence
        if (qualityAssessment.OverallScore < _opts.QualityReduceConfidenceThreshold)
        {
            var qualityPenalty = 1.0 - ((_opts.QualityReduceConfidenceThreshold - qualityAssessment.OverallScore) * 0.3);
            calibratedConfidence *= qualityPenalty;
        }

        // Adjust for sleeve detection
        if (failureResult.IsSleevedOrTopLoaded)
        {
            calibratedConfidence *= failureResult.Confidence;
        }

        var calibratedOverall = Math.Clamp(calibratedConfidence, 0.01, 0.99);

        // -- Step 7: Build overlay data --
        var overlay = BuildOverlay(cardQuad, src.Width, src.Height);

        // -- Step 8: Composite debug image --
        debugViz.DrawComposite(src, cardQuad, normalized, centeringResult.DetectedBorders,
            allCvDefects, centeringResult.Centering, imageType.ToString().ToLowerInvariant());

        var analysisMethod = hybridResult.MlAvailable
            ? "OpenCV-v4+ML-hybrid"
            : mlResult.DefectModelAvailable || mlResult.SurfaceModelAvailable
                ? "OpenCV-v4+ML"
                : "OpenCV-v4";

        var result = new ImageAnalysisResult
        {
            DetectedCentering = centeringResult.Centering,
            CornersScore = hybridResult.CornersScore,
            EdgesScore = hybridResult.EdgesScore,
            SurfaceScore = hybridResult.SurfaceScore,
            DetectedDefects = hybridResult.CombinedDefects,
            Overlay = overlay,
            AnalyzedAt = DateTimeOffset.UtcNow,
            AnalysisMethod = analysisMethod,
            // ML-augmented fields
            MlDetectedDefects = mlResult.Defects,
            MlSurfaceScore = mlResult.SurfaceScore,
            DefectModelUsed = mlResult.DefectModelAvailable,
            SurfaceModelUsed = mlResult.SurfaceModelAvailable,
            // Confidence fields
            ImageQualityScore = qualityAssessment.OverallScore,
            OverallConfidence = calibratedOverall,
            ConfidenceDetail = confidence with { Overall = calibratedOverall },
            // Phase 10-24 extended fields
            QualityAssessment = qualityAssessment,
            FailureDetection = failureResult,
            Regions = regions,
            Features = features,
            HybridMlUsed = hybridResult.MlAvailable,
            HybridMlConfidence = hybridResult.MlConfidence
        };

        // Log analysis data for future ML training
        dataLogger.LogAnalysis(submissionId, imageBytes, normalized, result);

        if (calibratedOverall < _opts.LowConfidenceThreshold)
        {
            logger.LogWarning(
                "Low confidence ({Confidence:F3}) for submission {SubmissionId}: {Summary}",
                calibratedOverall, submissionId, confidence.Summary);
        }

        return new ImageAnalysisOutcome(result);
    }

    /// <summary>
    /// Recalculates scores from user corrections: packages correction
    /// OuterGuides/InnerGuides + centering percentages directly into the result.
    /// </summary>
    public ImageAnalysisResult RecalculateFromCorrection(ImageAnalysisResult original, UserCorrection correction)
    {
        logger.LogInformation(
            "RecalculateFromCorrection: OuterGuides={OuterCount}, InnerGuides={InnerCount}, LR={LR:F1}%, TB={TB:F1}%",
            correction.OuterGuides.Count,
            correction.InnerGuides.Count,
            correction.LeftRightCenteringPercent,
            correction.TopBottomCenteringPercent);

        var overlay = new AnalysisOverlay
        {
            OuterGuides = correction.OuterGuides,
            InnerGuides = correction.InnerGuides,
            LeftRightCenteringPercent = correction.LeftRightCenteringPercent,
            TopBottomCenteringPercent = correction.TopBottomCenteringPercent
        };

        var centering = original.DetectedCentering is not null
            ? original.DetectedCentering with
            {
                LeftRightFront = Math.Round(correction.LeftRightCenteringPercent, 1),
                TopBottomFront = Math.Round(correction.TopBottomCenteringPercent, 1)
            }
            : new CenteringMeasurement
            {
                LeftRightFront = Math.Round(correction.LeftRightCenteringPercent, 1),
                TopBottomFront = Math.Round(correction.TopBottomCenteringPercent, 1),
                LeftRightBack = 50,
                TopBottomBack = 50
            };

        return CloneIndependent(new ImageAnalysisResult
        {
            DetectedCentering = centering,
            CornersScore = original.CornersScore,
            EdgesScore = original.EdgesScore,
            SurfaceScore = original.SurfaceScore,
            DetectedDefects = original.DetectedDefects,
            Overlay = overlay,
            AnalyzedAt = DateTimeOffset.UtcNow,
            AnalysisMethod = $"{original.AnalysisMethod}-corrected",
            // Preserve ML data from original analysis
            MlDetectedDefects = original.MlDetectedDefects,
            MlSurfaceScore = original.MlSurfaceScore,
            DefectModelUsed = original.DefectModelUsed,
            SurfaceModelUsed = original.SurfaceModelUsed,
            OverallConfidence = original.OverallConfidence,
            ConfidenceDetail = original.ConfidenceDetail,
            // Preserve extended fields
            QualityAssessment = original.QualityAssessment,
            FailureDetection = original.FailureDetection,
            Regions = original.Regions,
            Features = original.Features,
            HybridMlUsed = original.HybridMlUsed,
            HybridMlConfidence = original.HybridMlConfidence
        });
    }

    /// <summary>
    /// Deep-clones every nested owned entity in an <see cref="ImageAnalysisResult"/>
    /// so the returned graph shares NO record-typed instances with the input.
    /// </summary>
    private static ImageAnalysisResult CloneIndependent(ImageAnalysisResult source) => new()
    {
        DetectedCentering = source.DetectedCentering is null ? null : source.DetectedCentering with { },
        CornersScore = source.CornersScore,
        EdgesScore = source.EdgesScore,
        SurfaceScore = source.SurfaceScore,
        DetectedDefects = source.DetectedDefects.Select(d => d with { }).ToList(),
        Overlay = source.Overlay is null ? null : new AnalysisOverlay
        {
            OuterGuides = source.Overlay.OuterGuides.Select(p => p with { }).ToList(),
            InnerGuides = source.Overlay.InnerGuides.Select(p => p with { }).ToList(),
            LeftRightCenteringPercent = source.Overlay.LeftRightCenteringPercent,
            TopBottomCenteringPercent = source.Overlay.TopBottomCenteringPercent
        },
        AnalyzedAt = source.AnalyzedAt,
        AnalysisMethod = source.AnalysisMethod,
        MlDetectedDefects = source.MlDetectedDefects.Select(d => d with { }).ToList(),
        MlSurfaceScore = source.MlSurfaceScore,
        DefectModelUsed = source.DefectModelUsed,
        SurfaceModelUsed = source.SurfaceModelUsed,
        ImageQualityScore = source.ImageQualityScore,
        OverallConfidence = source.OverallConfidence,
        ConfidenceDetail = source.ConfidenceDetail is null ? null : source.ConfidenceDetail with
        {
            // List<string> is reference-shared after `with`; rebuild for safety.
        },
        QualityAssessment = source.QualityAssessment is null ? null : source.QualityAssessment with
        {
            Issues = [..source.QualityAssessment.Issues]
        },
        FailureDetection = source.FailureDetection is null ? null : source.FailureDetection with { },
        Regions = source.Regions is null ? null : source.Regions with
        {
            BorderRegion = source.Regions.BorderRegion with { },
            ArtworkRegion = source.Regions.ArtworkRegion with { },
            TextRegion = source.Regions.TextRegion with { },
            CornerZones = source.Regions.CornerZones.Select(r => r with { }).ToList(),
            EdgeZones = source.Regions.EdgeZones.Select(r => r with { }).ToList(),
            InnerRegion = source.Regions.InnerRegion with { }
        },
        Features = source.Features is null ? null : source.Features with
        {
            EdgeRoughness = [..source.Features.EdgeRoughness],
            CornerGeometry = [..source.Features.CornerGeometry],
            SurfaceVariance = [..source.Features.SurfaceVariance],
            SurfaceTexture = [..source.Features.SurfaceTexture],
            ColorHistogram = [..source.Features.ColorHistogram],
            BorderThickness = [..source.Features.BorderThickness],
            CenteringDeviation = [..source.Features.CenteringDeviation],
            CornerWhitening = [..source.Features.CornerWhitening],
            EdgeWhitening = [..source.Features.EdgeWhitening]
        },
        HybridMlUsed = source.HybridMlUsed,
        HybridMlConfidence = source.HybridMlConfidence
    };

    // -- Private helpers --

    private static AnalysisOverlay BuildOverlay(Point2f[] cardQuad, int imgWidth, int imgHeight)
    {
        const double Outer = 0.08;
        const double Inner = 0.13;

        var outer = new List<NormalizedPoint>
        {
            new() { X = Outer,     Y = Outer     }, new() { X = 1 - Outer, Y = Outer     },
            new() { X = 1 - Outer, Y = Outer     }, new() { X = 1 - Outer, Y = 1 - Outer },
            new() { X = 1 - Outer, Y = 1 - Outer }, new() { X = Outer,     Y = 1 - Outer },
            new() { X = Outer,     Y = 1 - Outer }, new() { X = Outer,     Y = Outer     }
        };

        var inner = new List<NormalizedPoint>
        {
            new() { X = Inner,     Y = Inner     }, new() { X = 1 - Inner, Y = Inner     },
            new() { X = 1 - Inner, Y = Inner     }, new() { X = 1 - Inner, Y = 1 - Inner },
            new() { X = 1 - Inner, Y = 1 - Inner }, new() { X = Inner,     Y = 1 - Inner },
            new() { X = Inner,     Y = 1 - Inner }, new() { X = Inner,     Y = Inner     }
        };

        var (lr, tb) = ComputeCenteringPercents(outer, inner);

        return new AnalysisOverlay
        {
            OuterGuides = outer,
            InnerGuides = inner,
            LeftRightCenteringPercent = lr,
            TopBottomCenteringPercent = tb
        };
    }

    private static AnalysisOverlay CreateDefaultOverlay()
    {
        const double Outer = 0.08;
        const double Inner = 0.13;

        var outer = new List<NormalizedPoint>
        {
            new() { X = Outer,     Y = Outer     }, new() { X = 1 - Outer, Y = Outer     },
            new() { X = 1 - Outer, Y = Outer     }, new() { X = 1 - Outer, Y = 1 - Outer },
            new() { X = 1 - Outer, Y = 1 - Outer }, new() { X = Outer,     Y = 1 - Outer },
            new() { X = Outer,     Y = 1 - Outer }, new() { X = Outer,     Y = Outer     }
        };

        var inner = new List<NormalizedPoint>
        {
            new() { X = Inner,     Y = Inner     }, new() { X = 1 - Inner, Y = Inner     },
            new() { X = 1 - Inner, Y = Inner     }, new() { X = 1 - Inner, Y = 1 - Inner },
            new() { X = 1 - Inner, Y = 1 - Inner }, new() { X = Inner,     Y = 1 - Inner },
            new() { X = Inner,     Y = 1 - Inner }, new() { X = Inner,     Y = Inner     }
        };

        return new AnalysisOverlay
        {
            OuterGuides = outer,
            InnerGuides = inner,
            LeftRightCenteringPercent = 50.0,
            TopBottomCenteringPercent = 50.0
        };
    }

    private static (double LR, double TB) ComputeCenteringPercents(
        List<NormalizedPoint> outer, List<NormalizedPoint> inner)
    {
        if (outer.Count < 8 || inner.Count < 8)
            return (50.0, 50.0);

        var outerMids = MidpointsOf(outer);
        var innerMids = MidpointsOf(inner);

        var left = innerMids[3].X - outerMids[3].X;
        var right = outerMids[1].X - innerMids[1].X;
        var top = innerMids[0].Y - outerMids[0].Y;
        var bottom = outerMids[2].Y - innerMids[2].Y;

        return (SafePercent(left, right), SafePercent(top, bottom));
    }

    private static List<NormalizedPoint> MidpointsOf(List<NormalizedPoint> pairs)
    {
        var mids = new List<NormalizedPoint>();
        for (var i = 0; i + 1 < pairs.Count; i += 2)
            mids.Add(Mid(pairs[i], pairs[i + 1]));
        return mids;
    }

    private static NormalizedPoint Mid(NormalizedPoint a, NormalizedPoint b) =>
        new() { X = (a.X + b.X) / 2.0, Y = (a.Y + b.Y) / 2.0 };

    private static double SafePercent(double a, double b)
    {
        var total = a + b;
        return total > 0 ? Math.Round(a / total * 100.0, 1) : 50.0;
    }

    private static NormalizedPoint NormalizePoint(Point2f p, int w, int h) =>
        new() { X = Math.Round(p.X / w, 4), Y = Math.Round(p.Y / h, 4) };

    private static async Task<byte[]> ReadStreamAsync(Stream stream, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        return ms.ToArray();
    }

    private static ImageAnalysisResult CreateEmptyResult() => new()
    {
        DetectedCentering = null,
        CornersScore = null,
        EdgesScore = null,
        SurfaceScore = null,
        DetectedDefects = [],
        AnalyzedAt = DateTimeOffset.UtcNow,
        AnalysisMethod = "OpenCV-v4"
    };
}
