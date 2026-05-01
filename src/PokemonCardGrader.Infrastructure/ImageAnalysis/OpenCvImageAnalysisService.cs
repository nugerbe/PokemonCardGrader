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
/// quality gate → detection → failure check → normalization → alignment →
/// centering → regions → condition → advanced defects → features →
/// ML inference → hybrid combine → confidence → overlay → debug.
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
            return EmptyOutcome();
        }

        logger.LogInformation("Image decoded: {Width}x{Height} pixels.", src.Width, src.Height);

        // ── Phase 10: Image Quality Gate ──
        var qualityAssessment = qualityAnalyzer.Assess(src);
        if (!qualityAssessment.PassedGate)
        {
            logger.LogWarning(
                "Image quality gate failed (score={Score:F2}): {Action}",
                qualityAssessment.OverallScore, qualityAssessment.RecommendedAction);

            return EmptyOutcome(CreateEmptyResult() with
            {
                QualityAssessment = qualityAssessment,
                ImageQualityScore = qualityAssessment.OverallScore
            });
        }

        // ── Step 1: Detect the card quadrilateral ──
        var detection = detector.DetectWithMetadata(src);
        var cardQuad = detection?.Quad;
        debugViz.DrawCardDetection(src, cardQuad, imageType.ToString().ToLowerInvariant());

        // ── Phase 21: Failure detection ──
        var failureResult = failureDetector.Detect(src, cardQuad, detection?.UsedFullFrameFallback ?? false);

        if (failureResult.HasBlockingFailure)
        {
            logger.LogWarning("Blocking failure: {Type} — {Description}",
                failureResult.FailureType, failureResult.Description);

            return EmptyOutcome(CreateEmptyResult() with
            {
                QualityAssessment = qualityAssessment,
                FailureDetection = failureResult,
                ImageQualityScore = qualityAssessment.OverallScore
            });
        }

        if (detection is null || cardQuad is null)
        {
            logger.LogWarning("Could not detect card quadrilateral in {Width}x{Height} image.", src.Width, src.Height);
            return EmptyOutcome(CreateEmptyResult() with
            {
                QualityAssessment = qualityAssessment,
                ImageQualityScore = qualityAssessment.OverallScore
            });
        }

        // ── Step 2: Perspective-correct to normalized rectangle ──
        using var rawNormalized = normalizer.Normalize(src, cardQuad);

        // ── Phase 11: Alignment refinement ──
        using var normalized = alignmentRefiner.Refine(rawNormalized);

        // ── Phase 12: Region segmentation ──
        var regions = regionSegmenter.Segment(normalized.Width, normalized.Height);

        // ── Step 3: Measure centering with border prior ──
        BorderPrior? prior = null;
        try { prior = await borderPrediction.GetBorderPriorAsync(ct); }
        catch (Exception ex) { logger.LogWarning(ex, "Failed to load border prior; proceeding without it."); }

        var centeringResult = centeringAnalyzer.Measure(normalized, imageType, prior);
        debugViz.DrawBorderLines(normalized, centeringResult.DetectedBorders ?? CreateDefaultBorders(), imageType.ToString().ToLowerInvariant());

        // ── Step 4: Analyze condition (surface, corners, edges, defects) via CV ──
        var condition = conditionAnalyzer.Analyze(normalized);
        debugViz.DrawDefects(normalized, condition.Defects, imageType.ToString().ToLowerInvariant());

        // ── Phase 13: Advanced defect analysis ──
        var advancedDefects = advancedDefectAnalyzer.Analyze(normalized, regions);

        // Merge advanced defects with standard CV defects
        var allCvDefects = new List<DetectedDefect>(condition.Defects);
        allCvDefects.AddRange(advancedDefects.AdvancedDefects);

        // ── Phase 14: Feature extraction ──
        var features = featureExtractor.Extract(normalized, regions, centeringResult.Centering);

        // ── Step 5: Run ML inference (defect detection + surface grading) ──
        var mlResult = await onnxInference.RunAsync(normalized, ct);

        // ── Phase 15: Hybrid ML/CV combination ──
        var hybridResult = await hybridCombiner.CombineAsync(
            condition.CornersScore, condition.EdgesScore, condition.SurfaceScore,
            allCvDefects, features, mlResult, ct);

        // ── Step 6: Compute pipeline confidence ──
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

        // ── Phase 20: Confidence calibration ──
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

        // ── Step 7: Build overlay data ──
        var overlay = BuildOverlay(cardQuad, src.Width, src.Height, centeringResult.DetectedBorders);

        // ── Step 7b: Encode the rectified card as JPEG so the worker can persist it.
        //           This is what the client renders as the primary "centering view"
        //           — the digital equivalent of laying the card under a Luxiv overlay.
        var normalizedJpeg = EncodeJpeg(normalized, _opts.NormalizedImageJpegQuality);

        // ── Step 8: Composite debug image ──
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

        return new ImageAnalysisOutcome(result, normalizedJpeg);
    }

    /// <summary>
    /// Recalculates scores from user corrections:
    /// - Adjusted borders -> recompute centering from border line positions.
    /// - Dismissed defects -> remove from list and improve edges/surface by reducing penalty.
    /// </summary>
    public ImageAnalysisResult RecalculateFromCorrection(ImageAnalysisResult original, UserCorrection correction)
    {
        var centering = original.DetectedCentering;
        var cornersScore = original.CornersScore;
        var edgesScore = original.EdgesScore;
        var surfaceScore = original.SurfaceScore;
        var defects = original.DetectedDefects;
        var overlay = original.Overlay;

        logger.LogInformation(
            "RecalculateFromCorrection: AdjustedBorders={HasBorders}, AdjustedBoundary={HasBoundary}, DismissedCount={DismissedCount}",
            correction.AdjustedBorders is not null,
            correction.AdjustedBoundary is { Count: > 0 },
            correction.DismissedDefectIndices.Count);

        // Update card boundary if user adjusted corners
        if (correction.AdjustedBoundary is { Count: 4 } && overlay is not null)
        {
            overlay = overlay with { CardBoundary = correction.AdjustedBoundary };
        }

        // Recalculate centering from adjusted border positions
        if (correction.AdjustedBorders is not null)
        {
            var borders = correction.AdjustedBorders;
            var leftWidth = borders.LeftBorderX;
            var rightWidth = 1.0 - borders.RightBorderX;
            var topHeight = borders.TopBorderY;
            var bottomHeight = 1.0 - borders.BottomBorderY;

            var totalHorizontal = leftWidth + rightWidth;
            var totalVertical = topHeight + bottomHeight;

            var lr = totalHorizontal > 0 ? (leftWidth / totalHorizontal) * 100.0 : 50.0;
            var tb = totalVertical > 0 ? (topHeight / totalVertical) * 100.0 : 50.0;

            logger.LogInformation(
                "RecalculateFromCorrection: Borders L={Left:F4} R={Right:F4} T={Top:F4} B={Bottom:F4} -> LR={LR:F1}% TB={TB:F1}%",
                borders.LeftBorderX, borders.RightBorderX, borders.TopBorderY, borders.BottomBorderY, lr, tb);

            centering = new CenteringMeasurement
            {
                LeftRightFront = Math.Round(lr, 1),
                TopBottomFront = Math.Round(tb, 1),
                LeftRightBack = centering?.LeftRightBack ?? 50,
                TopBottomBack = centering?.TopBottomBack ?? 50
            };

            overlay = overlay is not null
                ? overlay with { BorderLines = correction.AdjustedBorders }
                : null;
        }

        // Remove dismissed defects and improve scores
        if (correction.DismissedDefectIndices.Count > 0 && defects.Count > 0)
        {
            var originalDefectCount = defects.Count;
            var dismissed = correction.DismissedDefectIndices.ToHashSet();
            var keptDefects = defects
                .Where((_, idx) => !dismissed.Contains(idx))
                .ToList();

            var removedCount = originalDefectCount - keptDefects.Count;
            defects = keptDefects;

            var proportionDismissed = (double)removedCount / originalDefectCount;

            logger.LogInformation(
                "DismissedDefects: {Removed}/{Total} ({Proportion:P0}) edges={Edges:F1} surface={Surface:F1}",
                removedCount, originalDefectCount, proportionDismissed,
                edgesScore ?? 0, surfaceScore ?? 0);

            if (edgesScore.HasValue)
            {
                var gap = 10.0 - edgesScore.Value;
                edgesScore = Math.Clamp(edgesScore.Value + gap * proportionDismissed * 0.9, 1.0, 10.0);
                edgesScore = Math.Round(edgesScore.Value * 2) / 2.0;
            }

            if (surfaceScore.HasValue)
            {
                var gap = 10.0 - surfaceScore.Value;
                surfaceScore = Math.Clamp(surfaceScore.Value + gap * proportionDismissed * 0.9, 1.0, 10.0);
                surfaceScore = Math.Round(surfaceScore.Value * 2) / 2.0;
            }
        }

        return new ImageAnalysisResult
        {
            DetectedCentering = centering,
            CornersScore = cornersScore,
            EdgesScore = edgesScore,
            SurfaceScore = surfaceScore,
            DetectedDefects = defects,
            Overlay = overlay,
            AnalyzedAt = DateTimeOffset.UtcNow,
            AnalysisMethod = "OpenCV-v4-corrected",
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
        };
    }

    // ── Private helpers ──

    private static AnalysisOverlay BuildOverlay(
        Point2f[] cardQuad, int srcWidth, int srcHeight, BorderLines? detectedBorders)
    {
        var boundary = cardQuad.Select(p => new NormalizedPoint
        {
            X = Math.Round(p.X / srcWidth, 4),
            Y = Math.Round(p.Y / srcHeight, 4)
        }).ToList();

        var borderLines = detectedBorders ?? CreateDefaultBorders();

        return new AnalysisOverlay
        {
            CardBoundary = boundary,
            BorderLines = borderLines
        };
    }

    private static BorderLines CreateDefaultBorders() => new()
    {
        LeftBorderX = 0.05,
        RightBorderX = 0.95,
        TopBorderY = 0.05,
        BottomBorderY = 0.95
    };

    private static async Task<byte[]> ReadStreamAsync(Stream stream, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        return ms.ToArray();
    }

    /// <summary>
    /// Encodes a Mat as JPEG bytes. Returns null if encoding fails.
    /// Used to ship the rectified card image back to the client as the primary
    /// "centering view" — the surface for the digital grading overlay.
    /// </summary>
    private static byte[]? EncodeJpeg(Mat image, int quality)
    {
        if (image is null || image.Empty())
            return null;

        try
        {
            var qualityClamped = Math.Clamp(quality, 50, 100);
            var success = Cv2.ImEncode(
                ".jpg",
                image,
                out var buffer,
                [(int)ImwriteFlags.JpegQuality, qualityClamped]);
            return success ? buffer : null;
        }
        catch
        {
            return null;
        }
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

    private static ImageAnalysisOutcome EmptyOutcome() =>
        new(CreateEmptyResult(), null);

    private static ImageAnalysisOutcome EmptyOutcome(ImageAnalysisResult result) =>
        new(result, null);
}
