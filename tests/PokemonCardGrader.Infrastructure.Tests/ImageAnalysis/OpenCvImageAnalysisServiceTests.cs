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
    private const double BorderFractionTolerance = 0.015;

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
        var result = await _sut.AnalyzeImageAsync(imageStream, ImageType.Front, CancellationToken.None);

        // Assert
        Assert.NotNull(result.DetectedCentering);

        var centering = result.DetectedCentering;
        Assert.InRange(centering.LeftRightFront, expectedLR - CenteringTolerancePct, expectedLR + CenteringTolerancePct);
        Assert.InRange(centering.TopBottomFront, expectedTB - CenteringTolerancePct, expectedTB + CenteringTolerancePct);
    }

    #endregion

    #region Overlay and Border Lines

    [Fact]
    public async Task AnalyzeImageAsync_ReturnsNonNullOverlayWithBorderLines()
    {
        // Arrange - use a well-centered card
        var (stream, expected) = TestCardImageGenerator.CreateCardImage(25, 25, 35, 35);
        using var imageStream = stream;

        // Act
        var result = await _sut.AnalyzeImageAsync(imageStream, ImageType.Front, CancellationToken.None);

        // Assert
        Assert.NotNull(result.Overlay);
        Assert.NotNull(result.Overlay.CardBoundary);
        Assert.Equal(4, result.Overlay.CardBoundary.Count);

        var borders = result.Overlay.BorderLines;
        Assert.NotNull(borders);
        Assert.InRange(borders.LeftBorderX, expected.LeftBorderX - BorderFractionTolerance, expected.LeftBorderX + BorderFractionTolerance);
        Assert.InRange(borders.RightBorderX, expected.RightBorderX - BorderFractionTolerance, expected.RightBorderX + BorderFractionTolerance);
        Assert.InRange(borders.TopBorderY, expected.TopBorderY - BorderFractionTolerance, expected.TopBorderY + BorderFractionTolerance);
        Assert.InRange(borders.BottomBorderY, expected.BottomBorderY - BorderFractionTolerance, expected.BottomBorderY + BorderFractionTolerance);
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
        var result = await _sut.AnalyzeImageAsync(imageStream, ImageType.Front, CancellationToken.None);

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
    public void RecalculateFromCorrection_WithAdjustedBorders_RecomputesCentering()
    {
        // Arrange - original result with 50/50 centering
        var original = new ImageAnalysisResult
        {
            DetectedCentering = CenteringMeasurement.Perfect,
            CornersScore = 9.0,
            EdgesScore = 8.5,
            SurfaceScore = 9.5,
            DetectedDefects = [],
            Overlay = new AnalysisOverlay
            {
                CardBoundary =
                [
                    new NormalizedPoint { X = 0.1, Y = 0.1 },
                    new NormalizedPoint { X = 0.9, Y = 0.1 },
                    new NormalizedPoint { X = 0.9, Y = 0.9 },
                    new NormalizedPoint { X = 0.1, Y = 0.9 },
                ],
                BorderLines = new BorderLines
                {
                    LeftBorderX = 0.05,
                    RightBorderX = 0.95,
                    TopBorderY = 0.05,
                    BottomBorderY = 0.95,
                }
            },
            AnalyzedAt = DateTimeOffset.UtcNow,
            AnalysisMethod = "OpenCV-v4"
        };

        // Correction shifts borders to 60/40 LR (left=0.06, right border at 0.96 so right width=0.04)
        var correction = new UserCorrection
        {
            AdjustedBorders = new BorderLines
            {
                LeftBorderX = 0.06,
                RightBorderX = 0.96,
                TopBorderY = 0.05,
                BottomBorderY = 0.95,
            },
            DismissedDefectIndices = [],
        };

        // Act
        var result = _sut.RecalculateFromCorrection(original, correction);

        // Assert - LR should now be 60% (0.06 / (0.06 + 0.04) * 100)
        Assert.NotNull(result.DetectedCentering);
        Assert.Equal(60.0, result.DetectedCentering.LeftRightFront);
        Assert.Equal(50.0, result.DetectedCentering.TopBottomFront);
        Assert.Equal("OpenCV-v4-corrected", result.AnalysisMethod);
    }

    [Fact]
    public void RecalculateFromCorrection_WithNoChanges_ReturnsSameValues()
    {
        // Arrange
        var original = new ImageAnalysisResult
        {
            DetectedCentering = new CenteringMeasurement
            {
                LeftRightFront = 55.0,
                TopBottomFront = 48.0,
                LeftRightBack = 50,
                TopBottomBack = 50,
            },
            CornersScore = 8.0,
            EdgesScore = 7.5,
            SurfaceScore = 9.0,
            DetectedDefects =
            [
                new DetectedDefect
                {
                    Type = "scratch",
                    Severity = 0.3,
                    X = 0.5,
                    Y = 0.5,
                    Width = 0.1,
                    Height = 0.01,
                    Confidence = 0.6,
                }
            ],
            Overlay = new AnalysisOverlay
            {
                CardBoundary =
                [
                    new NormalizedPoint { X = 0.1, Y = 0.1 },
                    new NormalizedPoint { X = 0.9, Y = 0.1 },
                    new NormalizedPoint { X = 0.9, Y = 0.9 },
                    new NormalizedPoint { X = 0.1, Y = 0.9 },
                ],
                BorderLines = new BorderLines
                {
                    LeftBorderX = 0.055,
                    RightBorderX = 0.945,
                    TopBorderY = 0.048,
                    BottomBorderY = 0.952,
                }
            },
            AnalyzedAt = DateTimeOffset.UtcNow,
            AnalysisMethod = "OpenCV-v4"
        };

        // No corrections
        var correction = new UserCorrection
        {
            DismissedDefectIndices = [],
        };

        // Act
        var result = _sut.RecalculateFromCorrection(original, correction);

        // Assert - centering and scores should be unchanged
        Assert.NotNull(result.DetectedCentering);
        Assert.Equal(original.DetectedCentering.LeftRightFront, result.DetectedCentering.LeftRightFront);
        Assert.Equal(original.DetectedCentering.TopBottomFront, result.DetectedCentering.TopBottomFront);
        Assert.Equal(original.CornersScore, result.CornersScore);
        Assert.Equal(original.EdgesScore, result.EdgesScore);
        Assert.Equal(original.SurfaceScore, result.SurfaceScore);
        Assert.Single(result.DetectedDefects);
    }

    #endregion
}
