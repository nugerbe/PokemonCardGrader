using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PokemonCardGrader.Application.DTOs;
using PokemonCardGrader.Application.Interfaces;
using PokemonCardGrader.Application.Services;
using PokemonCardGrader.Application.Workers;
using PokemonCardGrader.Domain.Entities;
using PokemonCardGrader.Domain.Enums;
using PokemonCardGrader.Domain.ValueObjects;
using ImageType = PokemonCardGrader.Domain.Enums.ImageType;

namespace PokemonCardGrader.Application.Tests.Workers;

public sealed class ImageAnalysisWorkerTests
{
    private readonly Channel<ImageProcessingRequest> _channel;
    private readonly IImageStorageService _storageService;
    private readonly IImageAnalysisService _analysisService;
    private readonly ICardSubmissionRepository _submissionRepository;
    private readonly CardSubmissionService _submissionService;
    private readonly IGradeEstimationService _estimationService;
    private readonly ImageAnalysisWorker _sut;

    public ImageAnalysisWorkerTests()
    {
        _channel = Channel.CreateUnbounded<ImageProcessingRequest>();
        _storageService = Substitute.For<IImageStorageService>();
        _analysisService = Substitute.For<IImageAnalysisService>();
        _submissionRepository = Substitute.For<ICardSubmissionRepository>();
        _estimationService = Substitute.For<IGradeEstimationService>();

        var gradingResultRepository = Substitute.For<IGradingResultRepository>();
        _submissionService = new CardSubmissionService(
            _submissionRepository, _estimationService, gradingResultRepository, _storageService,
            _analysisService, NullLogger<CardSubmissionService>.Instance);

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IImageStorageService)).Returns(_storageService);
        serviceProvider.GetService(typeof(IImageAnalysisService)).Returns(_analysisService);
        serviceProvider.GetService(typeof(ICardSubmissionRepository)).Returns(_submissionRepository);
        serviceProvider.GetService(typeof(CardSubmissionService)).Returns(_submissionService);

        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        // IServiceScopeFactory.CreateAsyncScope() is an extension method that calls CreateScope()
        // so mocking CreateScope() is sufficient.

        var logger = NullLogger<ImageAnalysisWorker>.Instance;

        _sut = new ImageAnalysisWorker(_channel, scopeFactory, logger);
    }

    /// <summary>
    /// Wire <see cref="ICardSubmissionRepository.AddAnalysisRecordAsync"/> on the
    /// substitute so that any record passed for the supplied image is loaded into
    /// the image's in-memory navigation collection — mirroring what EF Core does
    /// when the principal entity is already tracked. Subsequent reads of
    /// <c>image.LatestAnalysisResult</c> will see the freshly-added record.
    /// </summary>
    private void WireAddAnalysisRecordToImage(CardImage image)
    {
        _submissionRepository.AddAnalysisRecordAsync(
                Arg.Do<ImageAnalysisRecord>(r =>
                {
                    if (r.CardImageId == image.Id)
                        image.LoadAnalysisRecordForTest(r);
                }),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task ProcessesImage_PersistsAnalysisResult_OnCardImage()
    {
        // Arrange
        var submissionId = Guid.NewGuid();
        var submission = CardSubmission.Create("user-123", Guid.NewGuid());
        var cardImage = CardImage.Create(submission.Id, "images/front.jpg", "front.jpg", ImageType.Front, 1024);

        var request = new ImageProcessingRequest
        {
            CardImageId = cardImage.Id,
            CardSubmissionId = submissionId,
            StoragePath = "images/front.jpg",
            ImageType = ImageType.Front
        };

        var analysisResult = new ImageAnalysisResult
        {
            DetectedCentering = CenteringMeasurement.Perfect,
            CornersScore = 9.0,
            EdgesScore = 8.5,
            SurfaceScore = 9.5,
            DetectedDefects = [],
            AnalyzedAt = DateTimeOffset.UtcNow,
            AnalysisMethod = "test-analysis"
        };

        _storageService.GetImageAsync("images/front.jpg", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Stream?>(new MemoryStream([1, 2, 3])));
        _analysisService.AnalyzeImageAsync(Arg.Any<Stream>(), Arg.Any<ImageType>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ImageAnalysisOutcome(analysisResult)));
        _submissionRepository.GetImageByIdAsync(cardImage.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CardImage?>(cardImage));
        _submissionRepository.GetImagesWithLatestAnalysisAsync(submissionId, Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(new List<CardImage> { cardImage }));
        _submissionRepository.GetByIdInternalAsync(submissionId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CardSubmission?>(submission));
        _estimationService.EstimateAllCompaniesAsync(
                submissionId, Arg.Any<ConditionScores>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<GradeEstimate>()));
        WireAddAnalysisRecordToImage(cardImage);

        // Act — write request then complete the channel so worker finishes
        await _channel.Writer.WriteAsync(request);
        _channel.Writer.Complete();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await _sut.StartAsync(cts.Token);
        await _sut.ExecuteTask!;

        // Assert — a new analysis record (Source=Initial) was appended for this image
        await _submissionRepository.Received(1).AddAnalysisRecordAsync(
            Arg.Is<ImageAnalysisRecord>(r =>
                r.CardImageId == cardImage.Id &&
                r.Source == AnalysisRecordSource.Initial &&
                r.Result.AnalysisMethod == "test-analysis"),
            Arg.Any<CancellationToken>());
        Assert.NotNull(cardImage.LatestAnalysisResult);
        Assert.Equal("test-analysis", cardImage.LatestAnalysisResult!.AnalysisMethod);
        // Scope 1: persist analysis result via SaveChangesAsync
        // Scope 2: estimates inserted via SaveChangesAsync (after DeleteEstimatesAsync + AddEstimatesAsync)
        await _submissionRepository.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
        // Scope 2: score update uses concurrency-resolving save
        await _submissionRepository.Received(1).SaveChangesResolvingConcurrencyAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessesImage_SetsImageDerivedScores_WhenCenteringDetected()
    {
        // Arrange
        var submissionId = Guid.NewGuid();
        var submission = CardSubmission.Create("user-123", Guid.NewGuid());
        var cardImage = CardImage.Create(submission.Id, "images/front.jpg", "front.jpg", ImageType.Front, 1024);

        var request = new ImageProcessingRequest
        {
            CardImageId = cardImage.Id,
            CardSubmissionId = submissionId,
            StoragePath = "images/front.jpg",
            ImageType = ImageType.Front
        };

        var analysisResult = new ImageAnalysisResult
        {
            DetectedCentering = CenteringMeasurement.Perfect,
            CornersScore = 9.0,
            EdgesScore = 8.5,
            SurfaceScore = 9.5,
            DetectedDefects = [],
            AnalyzedAt = DateTimeOffset.UtcNow,
            AnalysisMethod = "test-analysis"
        };

        _storageService.GetImageAsync("images/front.jpg", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Stream?>(new MemoryStream([1, 2, 3])));
        _analysisService.AnalyzeImageAsync(Arg.Any<Stream>(), Arg.Any<ImageType>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ImageAnalysisOutcome(analysisResult)));
        _submissionRepository.GetImageByIdAsync(cardImage.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CardImage?>(cardImage));
        _submissionRepository.GetImagesWithLatestAnalysisAsync(submissionId, Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(new List<CardImage> { cardImage }));
        _submissionRepository.GetByIdInternalAsync(submissionId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CardSubmission?>(submission));
        _estimationService.EstimateAllCompaniesAsync(
                submissionId, Arg.Any<ConditionScores>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<GradeEstimate>()));
        WireAddAnalysisRecordToImage(cardImage);
        // Act
        await _channel.Writer.WriteAsync(request);
        _channel.Writer.Complete();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await _sut.StartAsync(cts.Token);
        await _sut.ExecuteTask!;

        // Assert — image-derived scores set on submission via CombineAndSetImageScoresAsync
        Assert.NotNull(submission.ImageDerivedScores);
        Assert.Equal(9.0, submission.ImageDerivedScores.Corners);
        Assert.Equal(8.5, submission.ImageDerivedScores.Edges);
        Assert.Equal(9.5, submission.ImageDerivedScores.Surface);
        Assert.NotNull(submission.FinalScores);
    }

    [Fact]
    public async Task ProcessesImage_SetsDefaultScores_WhenNoCenteringDetected()
    {
        // Arrange
        var submissionId = Guid.NewGuid();
        var submission = CardSubmission.Create("user-123", Guid.NewGuid());
        var cardImage = CardImage.Create(submission.Id, "images/front.jpg", "front.jpg", ImageType.Front, 1024);

        var request = new ImageProcessingRequest
        {
            CardImageId = cardImage.Id,
            CardSubmissionId = submissionId,
            StoragePath = "images/front.jpg",
            ImageType = ImageType.Front
        };

        var analysisResult = new ImageAnalysisResult
        {
            DetectedCentering = null,
            CornersScore = null,
            EdgesScore = null,
            SurfaceScore = null,
            DetectedDefects = [],
            AnalyzedAt = DateTimeOffset.UtcNow,
            AnalysisMethod = "test-analysis"
        };

        _storageService.GetImageAsync("images/front.jpg", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Stream?>(new MemoryStream([1, 2, 3])));
        _analysisService.AnalyzeImageAsync(Arg.Any<Stream>(), Arg.Any<ImageType>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ImageAnalysisOutcome(analysisResult)));
        _submissionRepository.GetImageByIdAsync(cardImage.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CardImage?>(cardImage));
        _submissionRepository.GetImagesWithLatestAnalysisAsync(submissionId, Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(new List<CardImage> { cardImage }));
        _submissionRepository.GetByIdInternalAsync(submissionId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CardSubmission?>(submission));
        _estimationService.EstimateAllCompaniesAsync(
                submissionId, Arg.Any<ConditionScores>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<GradeEstimate>()));
        WireAddAnalysisRecordToImage(cardImage);
        // Act
        await _channel.Writer.WriteAsync(request);
        _channel.Writer.Complete();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await _sut.StartAsync(cts.Token);
        await _sut.ExecuteTask!;

        // Assert — analysis result saved; scores default to 50/50 centering and 8.0 condition
        Assert.NotNull(cardImage.LatestAnalysisResult);
        Assert.NotNull(submission.ImageDerivedScores);
        Assert.Equal(50.0, submission.ImageDerivedScores.Centering!.LeftRightFront);
        Assert.Equal(50.0, submission.ImageDerivedScores.Centering!.TopBottomFront);
        Assert.Equal(8.0, submission.ImageDerivedScores.Corners);
        Assert.Equal(8.0, submission.ImageDerivedScores.Edges);
        Assert.Equal(8.0, submission.ImageDerivedScores.Surface);
    }

    [Fact]
    public async Task ProcessesImage_SkipsGracefully_WhenImageNotFound()
    {
        // Arrange
        var request = new ImageProcessingRequest
        {
            CardImageId = Guid.NewGuid(),
            CardSubmissionId = Guid.NewGuid(),
            StoragePath = "images/missing.jpg",
            ImageType = ImageType.Front
        };

        _storageService.GetImageAsync("images/missing.jpg", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Stream?>(null));

        // Act
        await _channel.Writer.WriteAsync(request);
        _channel.Writer.Complete();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await _sut.StartAsync(cts.Token);
        await _sut.ExecuteTask!;

        // Assert — no further calls made
        await _analysisService.DidNotReceive().AnalyzeImageAsync(Arg.Any<Stream>(), Arg.Any<ImageType>(), Arg.Any<CancellationToken>());
        await _submissionRepository.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessesImage_SkipsGracefully_WhenCardImageNotFound()
    {
        // Arrange
        var request = new ImageProcessingRequest
        {
            CardImageId = Guid.NewGuid(),
            CardSubmissionId = Guid.NewGuid(),
            StoragePath = "images/front.jpg",
            ImageType = ImageType.Front
        };

        _storageService.GetImageAsync("images/front.jpg", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Stream?>(new MemoryStream([1, 2, 3])));
        _analysisService.AnalyzeImageAsync(Arg.Any<Stream>(), Arg.Any<ImageType>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ImageAnalysisOutcome(new ImageAnalysisResult
            {
                DetectedCentering = CenteringMeasurement.Perfect,
                CornersScore = 9.0,
                EdgesScore = 9.0,
                SurfaceScore = 9.0,
                DetectedDefects = [],
                AnalyzedAt = DateTimeOffset.UtcNow,
                AnalysisMethod = "test"
            })));
        _submissionRepository.GetImageByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CardImage?>(null));

        // Act
        await _channel.Writer.WriteAsync(request);
        _channel.Writer.Complete();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await _sut.StartAsync(cts.Token);
        await _sut.ExecuteTask!;

        // Assert — no save since card image not found
        await _submissionRepository.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessesImage_UsesDefaultScores_WhenSubScoresNull()
    {
        // Arrange
        var submissionId = Guid.NewGuid();
        var submission = CardSubmission.Create("user-123", Guid.NewGuid());
        var cardImage = CardImage.Create(submission.Id, "images/front.jpg", "front.jpg", ImageType.Front, 1024);

        var request = new ImageProcessingRequest
        {
            CardImageId = cardImage.Id,
            CardSubmissionId = submissionId,
            StoragePath = "images/front.jpg",
            ImageType = ImageType.Front
        };

        var analysisResult = new ImageAnalysisResult
        {
            DetectedCentering = CenteringMeasurement.Perfect,
            CornersScore = null,
            EdgesScore = null,
            SurfaceScore = null,
            DetectedDefects = [],
            AnalyzedAt = DateTimeOffset.UtcNow,
            AnalysisMethod = "test-analysis"
        };

        _storageService.GetImageAsync("images/front.jpg", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Stream?>(new MemoryStream([1, 2, 3])));
        _analysisService.AnalyzeImageAsync(Arg.Any<Stream>(), Arg.Any<ImageType>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ImageAnalysisOutcome(analysisResult)));
        _submissionRepository.GetImageByIdAsync(cardImage.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CardImage?>(cardImage));
        _submissionRepository.GetImagesWithLatestAnalysisAsync(submissionId, Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(new List<CardImage> { cardImage }));
        _submissionRepository.GetByIdInternalAsync(submissionId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CardSubmission?>(submission));
        _estimationService.EstimateAllCompaniesAsync(
                submissionId, Arg.Any<ConditionScores>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<GradeEstimate>()));
        WireAddAnalysisRecordToImage(cardImage);
        // Act
        await _channel.Writer.WriteAsync(request);
        _channel.Writer.Complete();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await _sut.StartAsync(cts.Token);
        await _sut.ExecuteTask!;

        // Assert — defaults to 8.0 for null sub-scores
        Assert.NotNull(submission.ImageDerivedScores);
        Assert.Equal(8.0, submission.ImageDerivedScores.Corners);
        Assert.Equal(8.0, submission.ImageDerivedScores.Edges);
        Assert.Equal(8.0, submission.ImageDerivedScores.Surface);
    }
}
