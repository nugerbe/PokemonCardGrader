using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using PokemonCardGrader.Application.Configuration;
using PokemonCardGrader.Application.Interfaces;
using PokemonCardGrader.Domain.Enums;
using PokemonCardGrader.Domain.ValueObjects;
using PokemonCardGrader.Infrastructure.ImageAnalysis;
using PokemonCardGrader.Infrastructure.ML;

namespace PokemonCardGrader.Infrastructure.Tests.ImageAnalysis;

public sealed class OpenCvImageAnalysisServiceTests
{
    private const double CenteringTolerancePct = 5.5;

    private readonly OpenCvImageAnalysisService _sut;

    public OpenCvImageAnalysisServiceTests()
    {
        var opts = Options.Create(new CardAnalysisOptions());
        var detector = new CardDetector(opts, Substitute.For<ILogger<CardDetector>>());
        var normalizer = new CardNormalizer(opts);
        var centeringAnalyzer = new CenteringAnalyzer(opts, Substitute.For<ILogger<CenteringAnalyzer>>());
        var conditionAnalyzer = new ConditionAnalyzer(opts, Substitute.For<ILogger<ConditionAnalyzer>>());
        var modelRegistry = new MLModelRegistry();
        var onnxInference = new OnnxInferenceService(modelRegistry, opts, Substitute.For<ILogger<OnnxInferenceService>>());
        var confidenceScorer = new ConfidenceScorer(opts, Substitute.For<ILogger<ConfidenceScorer>>());
        var debugViz = new DebugVisualizer(opts, Substitute.For<ILogger<DebugVisualizer>>());
        var dataLogger = new AnalysisDataLogger(opts, Substitute.For<ILogger<AnalysisDataLogger>>());
        var borderPrediction = Substitute.For<IBorderPredictionService>();
        borderPrediction.GetBorderPriorAsync(Arg.Any<CancellationToken>())
            .Returns((BorderPrior?)null);
        var qualityAnalyzer = new ImageQualityAnalyzer(opts, Substitute.For<ILogger<ImageQualityAnalyzer>>());
        var failureDetector = new FailureDetector(opts, Substitute.For<ILogger<FailureDetector>>());
        var alignmentRefiner = new AlignmentRefiner(opts, Substitute.For<ILogger<AlignmentRefiner>>());
        var regionSegmenter = new RegionSegmenter(opts);
        var advancedDefectAnalyzer = new AdvancedDefectAnalyzer(opts, Substitute.For<ILogger<AdvancedDefectAnalyzer>>());
        var featureExtractor = new FeatureExtractor(opts, Substitute.For<ILogger<FeatureExtractor>>());
        var hybridCombiner = new HybridScoreCombiner(modelRegistry, opts, Substitute.For<ILogger<HybridScoreCombiner>>());
        var confidenceCalibrator = new ConfidenceCalibrator(
            new CalibrationService(opts, Substitute.For<ILogger<CalibrationService>>()),
            Substitute.For<ILogger<ConfidenceCalibrator>>());
        var logger = Substitute.For<ILogger<OpenCvImageAnalysisService>>();

        _sut = new OpenCvImageAnalysisService(
            opts, detector, normalizer, centeringAnalyzer, conditionAnalyzer,
            onnxInference, confidenceScorer,
            debugViz, dataLogger, borderPrediction,
            qualityAnalyzer, failureDetector, alignmentRefiner,
            regionSegmenter, advancedDefectAnalyzer, featureExtractor,
            hybridCombiner, confidenceCalibrator, logger);
    }

    #region Centering Theory Tests

    public static TheoryData<int, int, int, int, double, double> CenteringTestCases => new()
    {
        //  leftPx, rightPx, topPx, bottomPx, expectedLR, expectedTB
        {   25,     25,      35,    35,        50.0,       50.0      }, // Perfect centering
        {   30,     20,      35,    35,        60.0,       50.0      }, // Moderate LR off-center
        {   35,     15,      35,    35,        70.0,       50.0      }, // Significant LR off-center
        {   30,     20,      38,    32,        60.0,       54.3      }, // Both axes off
        {   60,     60,      84,    84,        50.0,       50.0      }, // Large borders centered
        {   75,     25,      35,    35,        75.0,       50.0      }, // Near max border fraction
        {   20,     20,      21,    21,        50.0,       50.0      }, // Small borders centered
    };

    [Theory]
    [MemberData(nameof(CenteringTestCases))]
    public async Task AnalyzeImageAsync_DetectsCentering_WithinTolerance(
        int leftPx, int rightPx, int topPx, int bottomPx,
        double expectedLR, double expectedTB)
    {
        // Arrange
        var (stream, expected) = TestCardImageGenerator.CreateCardImage(leftPx, rightPx, topPx, bottomPx);
        using var imageStream = stream;

        // Act
        var result = (await _sut.AnalyzeImageAsync(imageStream, ImageType.Front, CancellationToken.None)).Result;

        // Assert
        Assert.NotNull(result.DetectedCentering);

        var centering = result.DetectedCentering;
        Assert.InRange(centering.LeftRightFront, expectedLR - CenteringTolerancePct, expectedLR + CenteringTolerancePct);
        Assert.InRange(centering.TopBottomFront, expectedTB - CenteringTolerancePct, expectedTB + CenteringTolerancePct);
    }

    #endregion

    #region Overlay and Border Lines

    [Fact]
    public async Task AnalyzeImageAsync_ReturnsOverlayWithEightOuterAndInnerEndpoints()
    {
        var (stream, _) = TestCardImageGenerator.CreateCardImage(25, 25, 35, 35);
        using var imageStream = stream;

        var result = (await _sut.AnalyzeImageAsync(imageStream, ImageType.Front, CancellationToken.None)).Result;

        Assert.NotNull(result.Overlay);
        Assert.Equal(8, result.Overlay.OuterGuides.Count);
        Assert.Equal(8, result.Overlay.InnerGuides.Count);
        Assert.InRange(result.Overlay.LeftRightCenteringPercent, 50.0, 100.0);
        Assert.InRange(result.Overlay.TopBottomCenteringPercent, 50.0, 100.0);

        // Outer guides should sit near the card edges (within 0-1 image space).
        Assert.All(result.Overlay.OuterGuides, p =>
        {
            Assert.InRange(p.X, 0.0, 1.0);
            Assert.InRange(p.Y, 0.0, 1.0);
        });

        // Inner guides should sit inside the outer-guide bbox.
        var outerMinX = result.Overlay.OuterGuides.Min(p => p.X);
        var outerMaxX = result.Overlay.OuterGuides.Max(p => p.X);
        var outerMinY = result.Overlay.OuterGuides.Min(p => p.Y);
        var outerMaxY = result.Overlay.OuterGuides.Max(p => p.Y);
        Assert.All(result.Overlay.InnerGuides, p =>
        {
            Assert.InRange(p.X, outerMinX, outerMaxX);
            Assert.InRange(p.Y, outerMinY, outerMaxY);
        });
    }

    #endregion

    #region Score Categories

    [Fact]
    public async Task AnalyzeImageAsync_ReturnsScoresForAllCategories()
    {
        // Arrange
        var (stream, _) = TestCardImageGenerator.CreateCardImage(25, 25, 35, 35);
        using var imageStream = stream;

        // Act
        var result = (await _sut.AnalyzeImageAsync(imageStream, ImageType.Front, CancellationToken.None)).Result;

        // Assert
        Assert.NotNull(result.CornersScore);
        Assert.NotNull(result.EdgesScore);
        Assert.NotNull(result.SurfaceScore);
        Assert.InRange(result.CornersScore.Value, 1.0, 10.0);
        Assert.InRange(result.EdgesScore.Value, 1.0, 10.0);
        Assert.InRange(result.SurfaceScore.Value, 1.0, 10.0);
    }

    #endregion

    #region Empty / Bad Input

    [Fact]
    public async Task AnalyzeImageAsync_WithEmptyStream_ThrowsArgumentException()
    {
        // Arrange
        using var emptyStream = new MemoryStream();

        // Act & Assert - OpenCV's Mat.FromImageData throws on empty byte array
        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.AnalyzeImageAsync(emptyStream, ImageType.Front, CancellationToken.None));
    }

    #endregion

    #region RecalculateFromCorrection

    [Fact]
    public void RecalculateFromCorrection_AppliesUserCenteringPercentsAndPreservesOriginalScores()
    {
        var original = new ImageAnalysisResult
        {
            DetectedCentering = new CenteringMeasurement
            {
                LeftRightFront = 50.0,
                TopBottomFront = 50.0,
                LeftRightBack = 60.0,
                TopBottomBack = 55.0
            },
            CornersScore = 9.0,
            EdgesScore = 8.5,
            SurfaceScore = 9.5,
            DetectedDefects = [],
            Overlay = MakeOverlay(50.0, 50.0),
            AnalyzedAt = DateTimeOffset.UtcNow,
            AnalysisMethod = "OpenCV-v4"
        };

        var correction = new UserCorrection
        {
            CardImageId = Guid.NewGuid(),
            OuterGuides = SquareEndpoints(0.10, 0.90, 0.10, 0.90),
            InnerGuides = SquareEndpoints(0.16, 0.94, 0.14, 0.86),  // off-centre
            LeftRightCenteringPercent = 60.0,
            TopBottomCenteringPercent = 55.0
        };

        var result = _sut.RecalculateFromCorrection(original, correction);

        Assert.NotNull(result.DetectedCentering);
        Assert.Equal(60.0, result.DetectedCentering.LeftRightFront);
        Assert.Equal(55.0, result.DetectedCentering.TopBottomFront);
        // Back-axis values are NOT reset by a front-of-card correction
        Assert.Equal(60.0, result.DetectedCentering.LeftRightBack);
        Assert.Equal(55.0, result.DetectedCentering.TopBottomBack);

        // Condition scores survive untouched
        Assert.Equal(9.0, result.CornersScore);
        Assert.Equal(8.5, result.EdgesScore);
        Assert.Equal(9.5, result.SurfaceScore);

        // Overlay carries the new endpoints and the new percentages
        Assert.NotNull(result.Overlay);
        Assert.Equal(8, result.Overlay.OuterGuides.Count);
        Assert.Equal(8, result.Overlay.InnerGuides.Count);
        Assert.Equal(60.0, result.Overlay.LeftRightCenteringPercent);
        Assert.Equal(55.0, result.Overlay.TopBottomCenteringPercent);

        Assert.Equal("OpenCV-v4-corrected", result.AnalysisMethod);
    }

    [Fact]
    public void RecalculateFromCorrection_ReturnsIndependentReferenceGraph()
    {
        var original = new ImageAnalysisResult
        {
            DetectedCentering = CenteringMeasurement.Perfect,
            CornersScore = 8.0,
            EdgesScore = 8.0,
            SurfaceScore = 8.0,
            DetectedDefects = [],
            Overlay = MakeOverlay(50.0, 50.0),
            AnalyzedAt = DateTimeOffset.UtcNow,
            AnalysisMethod = "OpenCV-v4"
        };

        var correction = new UserCorrection
        {
            CardImageId = Guid.NewGuid(),
            OuterGuides = SquareEndpoints(0.10, 0.90, 0.10, 0.90),
            InnerGuides = SquareEndpoints(0.15, 0.85, 0.15, 0.85),
            LeftRightCenteringPercent = 50.0,
            TopBottomCenteringPercent = 50.0
        };

        var result = _sut.RecalculateFromCorrection(original, correction);

        Assert.NotSame(original.Overlay, result.Overlay);
        Assert.NotSame(original.Overlay!.OuterGuides, result.Overlay!.OuterGuides);
        Assert.NotSame(original.Overlay.InnerGuides, result.Overlay.InnerGuides);
    }

    /// <summary>
    /// Builds the canonical 8-endpoint outer/inner guide pair tracing a
    /// rectangle [xMin, xMax] x [yMin, yMax] in image-relative coordinates.
    /// </summary>
    private static List<NormalizedPoint> SquareEndpoints(double xMin, double xMax, double yMin, double yMax)
    {
        var tl = new NormalizedPoint { X = xMin, Y = yMin };
        var tr = new NormalizedPoint { X = xMax, Y = yMin };
        var br = new NormalizedPoint { X = xMax, Y = yMax };
        var bl = new NormalizedPoint { X = xMin, Y = yMax };
        return [tl, tr, tr, br, br, bl, bl, tl];
    }

    private static AnalysisOverlay MakeOverlay(double lr, double tb) => new()
    {
        OuterGuides = SquareEndpoints(0.05, 0.95, 0.05, 0.95),
        InnerGuides = SquareEndpoints(0.10, 0.90, 0.10, 0.90),
        LeftRightCenteringPercent = lr,
        TopBottomCenteringPercent = tb
    };

    #endregion
}
