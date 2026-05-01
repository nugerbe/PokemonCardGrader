using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PokemonCardGrader.Application.Interfaces;
using PokemonCardGrader.Application.Services;
using PokemonCardGrader.Domain.Entities;
using PokemonCardGrader.Domain.Enums;
using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Application.Tests.Services;

public sealed class CardSubmissionServiceTests
{
    private readonly ICardSubmissionRepository _submissionRepository;
    private readonly IGradeEstimationService _estimationService;
    private readonly IGradingResultRepository _gradingResultRepository;
    private readonly IImageStorageService _imageStorageService;
    private readonly IImageAnalysisService _imageAnalysisService;
    private readonly CardSubmissionService _sut;

    public CardSubmissionServiceTests()
    {
        _submissionRepository = Substitute.For<ICardSubmissionRepository>();
        _estimationService = Substitute.For<IGradeEstimationService>();
        _gradingResultRepository = Substitute.For<IGradingResultRepository>();
        _imageStorageService = Substitute.For<IImageStorageService>();
        _imageAnalysisService = Substitute.For<IImageAnalysisService>();
        _sut = new CardSubmissionService(
            _submissionRepository, _estimationService, _gradingResultRepository, _imageStorageService,
            _imageAnalysisService, NullLogger<CardSubmissionService>.Instance);
    }

    [Fact]
    public async Task CreateSubmissionAsync_CreatesNewSubmission()
    {
        // Arrange
        var userId = "user-123";
        var cardId = Guid.NewGuid();
        _submissionRepository.AddAsync(Arg.Any<CardSubmission>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(callInfo.Arg<CardSubmission>()));

        // Act
        var result = await _sut.CreateSubmissionAsync(userId, cardId, "Test notes");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(userId, result.UserId);
        Assert.Equal(cardId, result.PokemonCardId);
        Assert.Equal("Test notes", result.Notes);
        await _submissionRepository.Received(1).AddAsync(Arg.Any<CardSubmission>(), Arg.Any<CancellationToken>());
        await _submissionRepository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateSubmissionAsync_WithoutNotes_CreatesSubmission()
    {
        // Arrange
        var userId = "user-123";
        var cardId = Guid.NewGuid();

        // Act
        var result = await _sut.CreateSubmissionAsync(userId, cardId);

        // Assert
        Assert.NotNull(result);
        Assert.Null(result.Notes);
    }

    [Fact]
    public async Task CreateSubmissionAsync_PropagatesCancellationToken()
    {
        // Arrange
        var cts = new CancellationTokenSource();

        // Act
        await _sut.CreateSubmissionAsync("user", Guid.NewGuid(), null, cts.Token);

        // Assert
        await _submissionRepository.Received(1).SaveChangesAsync(cts.Token);
    }

    [Fact]
    public async Task GetSubmissionAsync_ReturnsSubmission()
    {
        // Arrange
        var submissionId = Guid.NewGuid();
        var userId = "user-123";
        var submission = CardSubmission.Create(userId, Guid.NewGuid());

        _submissionRepository.GetByIdAsync(submissionId, userId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CardSubmission?>(submission));

        // Act
        var result = await _sut.GetSubmissionAsync(submissionId, userId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(userId, result.UserId);
    }

    [Fact]
    public async Task GetSubmissionAsync_WhenNotFound_ReturnsNull()
    {
        // Arrange
        _submissionRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CardSubmission?>(null));

        // Act
        var result = await _sut.GetSubmissionAsync(Guid.NewGuid(), "user-123");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetUserSubmissionsAsync_ReturnsSubmissions()
    {
        // Arrange
        var userId = "user-123";
        var submissions = new List<CardSubmission>
        {
            CardSubmission.Create(userId, Guid.NewGuid()),
            CardSubmission.Create(userId, Guid.NewGuid())
        };

        _submissionRepository.GetByUserIdAsync(userId, 1, 20, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(submissions));

        // Act
        var result = await _sut.GetUserSubmissionsAsync(userId, 1, 20);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetUserSubmissionsAsync_WithDefaultPaging_UsesDefaults()
    {
        // Arrange
        var userId = "user-123";
        _submissionRepository.GetByUserIdAsync(userId, 1, 20, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<CardSubmission>()));

        // Act
        await _sut.GetUserSubmissionsAsync(userId);

        // Assert
        await _submissionRepository.Received(1).GetByUserIdAsync(userId, 1, 20, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetManualScoresAndEstimateAsync_UpdatesScoresAndTriggersEstimation()
    {
        // Arrange
        var submissionId = Guid.NewGuid();
        var userId = "user-123";
        var submission = CardSubmission.Create(userId, Guid.NewGuid());
        var scores = new ConditionScores
        {
            Centering = CenteringMeasurement.Perfect,
            Corners = 9.5,
            Edges = 9.0,
            Surface = 9.5
        };

        var estimates = new List<GradeEstimate>
        {
            GradeEstimate.Create(submissionId, GradingCompany.PSA, 9.0, new Dictionary<string, double>(), 0.85, true, "PSA 9")
        };

        _submissionRepository.GetByIdAsync(submissionId, userId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CardSubmission?>(submission));
        _estimationService.EstimateAllCompaniesAsync(submissionId, Arg.Any<ConditionScores>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(estimates));

        // Act
        await _sut.SetManualScoresAndEstimateAsync(submissionId, userId, scores);

        // Assert
        Assert.NotNull(submission.ManualScores);
        Assert.Equal(9.5, submission.ManualScores.Corners);
        Assert.NotNull(submission.FinalScores);
        await _submissionRepository.Received(1).SaveChangesResolvingConcurrencyAsync(Arg.Any<CancellationToken>());
        await _submissionRepository.Received(1).DeleteEstimatesAsync(submissionId, Arg.Any<CancellationToken>());
        await _estimationService.Received(1).EstimateAllCompaniesAsync(submissionId, Arg.Any<ConditionScores>(), Arg.Any<CancellationToken>());
        await _submissionRepository.Received(1).AddEstimatesAsync(Arg.Any<IEnumerable<GradeEstimate>>(), Arg.Any<CancellationToken>());
        await _submissionRepository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetManualScoresAndEstimateAsync_WhenSubmissionNotFound_ThrowsException()
    {
        // Arrange
        _submissionRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CardSubmission?>(null));

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.SetManualScoresAndEstimateAsync(Guid.NewGuid(), "user", ConditionScores.Perfect));
        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public async Task SetFinalScoresAndEstimateAsync_UpdatesScoresAndTriggersEstimation()
    {
        // Arrange
        var submissionId = Guid.NewGuid();
        var userId = "user-123";
        var submission = CardSubmission.Create(userId, Guid.NewGuid());
        var scores = new ConditionScores
        {
            Centering = CenteringMeasurement.Perfect,
            Corners = 10,
            Edges = 10,
            Surface = 10
        };

        var estimates = new List<GradeEstimate>
        {
            GradeEstimate.Create(submissionId, GradingCompany.PSA, 10.0, new Dictionary<string, double>(), 0.95, true, "PSA 10")
        };

        _submissionRepository.GetByIdAsync(submissionId, userId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CardSubmission?>(submission));
        _estimationService.EstimateAllCompaniesAsync(submissionId, scores, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(estimates));

        // Act
        await _sut.SetFinalScoresAndEstimateAsync(submissionId, userId, scores);

        // Assert
        Assert.NotNull(submission.FinalScores);
        Assert.Equal(10, submission.FinalScores.Corners);
        await _submissionRepository.Received(1).SaveChangesResolvingConcurrencyAsync(Arg.Any<CancellationToken>());
        await _submissionRepository.Received(1).DeleteEstimatesAsync(submissionId, Arg.Any<CancellationToken>());
        await _estimationService.Received(1).EstimateAllCompaniesAsync(submissionId, scores, Arg.Any<CancellationToken>());
        await _submissionRepository.Received(1).AddEstimatesAsync(Arg.Any<IEnumerable<GradeEstimate>>(), Arg.Any<CancellationToken>());
        await _submissionRepository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetFinalScoresAndEstimateAsync_WhenSubmissionNotFound_ThrowsException()
    {
        // Arrange
        _submissionRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CardSubmission?>(null));

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.SetFinalScoresAndEstimateAsync(Guid.NewGuid(), "user", ConditionScores.Perfect));
        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public async Task RecordActualGradeAsync_CreatesGradingResultAndUpdatesSubmission()
    {
        // Arrange
        var submissionId = Guid.NewGuid();
        var userId = "user-123";
        var submission = CardSubmission.Create(userId, Guid.NewGuid());
        var subGrades = new Dictionary<string, double>
        {
            ["Centering"] = 10,
            ["Corners"] = 9.5,
            ["Edges"] = 9.5,
            ["Surface"] = 10
        };

        _submissionRepository.GetByIdAsync(submissionId, userId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CardSubmission?>(submission));

        _gradingResultRepository.AddAsync(Arg.Any<GradingResult>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(callInfo.Arg<GradingResult>()));

        // Act
        await _sut.RecordActualGradeAsync(
            submissionId, userId, GradingCompany.PSA, 9.5, subGrades, "PSA12345678");

        // Assert
        Assert.NotNull(submission.ActualResult);
        Assert.Equal(GradingCompany.PSA, submission.ActualResult.Company);
        Assert.Equal(9.5, submission.ActualResult.ActualGrade);
        Assert.Equal("PSA12345678", submission.ActualResult.CertificationNumber);
        await _gradingResultRepository.Received(1).AddAsync(Arg.Any<GradingResult>(), Arg.Any<CancellationToken>());
        await _submissionRepository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordActualGradeAsync_WithoutCertificationNumber_CreatesResult()
    {
        // Arrange
        var submissionId = Guid.NewGuid();
        var userId = "user-123";
        var submission = CardSubmission.Create(userId, Guid.NewGuid());

        _submissionRepository.GetByIdAsync(submissionId, userId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CardSubmission?>(submission));

        // Act
        await _sut.RecordActualGradeAsync(
            submissionId, userId, GradingCompany.BGS, 9.0, new Dictionary<string, double>(), null);

        // Assert
        Assert.NotNull(submission.ActualResult);
        Assert.Null(submission.ActualResult.CertificationNumber);
    }

    [Fact]
    public async Task RecordActualGradeAsync_WhenSubmissionNotFound_ThrowsException()
    {
        // Arrange
        _submissionRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CardSubmission?>(null));

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.RecordActualGradeAsync(
                Guid.NewGuid(), "user", GradingCompany.PSA, 10, new Dictionary<string, double>(), null));
        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public void MapToDto_MapsAllProperties()
    {
        // Arrange
        var cardId = Guid.NewGuid();
        var card = PokemonCard.CreateFromApi(
            "xy1-1", "Pikachu", "XY Base", "xy1", "42", CardRarity.Common, "image.jpg");

        var submission = CardSubmission.Create("user-123", cardId);

        // Use reflection to set the PokemonCard property
        var pokemonCardProperty = typeof(CardSubmission).GetProperty("PokemonCard");
        pokemonCardProperty!.SetValue(submission, card);

        var estimate = GradeEstimate.Create(
            submission.Id, GradingCompany.PSA, 9.0,
            new Dictionary<string, double> { ["Overall"] = 9.0 },
            0.85, true, "PSA 9");

        submission.SetEstimates(new[] { estimate });
        submission.SetFinalScores(ConditionScores.Perfect);

        var result = GradingResult.Create(
            submission.Id, "user-123", GradingCompany.PSA, 9.0,
            new Dictionary<string, double> { ["Overall"] = 9.0 }, "CERT123");
        submission.RecordActualResult(result);

        // Act
        var dto = _sut.MapToDto(submission);

        // Assert
        Assert.NotNull(dto);
        Assert.Equal(submission.Id, dto.Id);
        Assert.Equal("Pikachu", dto.CardName);
        Assert.Equal("XY Base", dto.SetName);
        Assert.Equal("42", dto.Number);
        Assert.Equal("image.jpg", dto.CardImageUrl);
        Assert.NotNull(dto.FinalScores);
        Assert.Single(dto.Estimates);
        Assert.Equal(GradingCompany.PSA, dto.Estimates[0].Company);
        Assert.Equal(9.0, dto.Estimates[0].PredictedGrade);
        Assert.NotNull(dto.ActualResult);
        Assert.Equal(9.0, dto.ActualResult.ActualGrade);
        Assert.Equal("CERT123", dto.ActualResult.CertificationNumber);
    }

    [Fact]
    public void MapToDto_WithoutActualResult_MapsCorrectly()
    {
        // Arrange
        var card = PokemonCard.CreateFromApi(
            "xy1-1", "Pikachu", "XY Base", "xy1", "42", CardRarity.Common, null);

        var submission = CardSubmission.Create("user-123", Guid.NewGuid());

        var pokemonCardProperty = typeof(CardSubmission).GetProperty("PokemonCard");
        pokemonCardProperty!.SetValue(submission, card);

        // Act
        var dto = _sut.MapToDto(submission);

        // Assert
        Assert.NotNull(dto);
        Assert.Null(dto.ActualResult);
        Assert.Empty(dto.Estimates);
    }

    [Fact]
    public void MapToDto_WithMultipleEstimates_MapsAll()
    {
        // Arrange
        var card = PokemonCard.CreateFromApi(
            "xy1-1", "Pikachu", "XY Base", "xy1", "42", CardRarity.Common, null);

        var submission = CardSubmission.Create("user-123", Guid.NewGuid());

        var pokemonCardProperty = typeof(CardSubmission).GetProperty("PokemonCard");
        pokemonCardProperty!.SetValue(submission, card);

        var estimates = new[]
        {
            GradeEstimate.Create(submission.Id, GradingCompany.PSA, 9.0, new Dictionary<string, double>(), 0.85, true, "PSA 9"),
            GradeEstimate.Create(submission.Id, GradingCompany.BGS, 9.5, new Dictionary<string, double>(), 0.90, false, "BGS 9.5"),
            GradeEstimate.Create(submission.Id, GradingCompany.CGC, 9.0, new Dictionary<string, double>(), 0.80, true, "CGC 9")
        };

        submission.SetEstimates(estimates);

        // Act
        var dto = _sut.MapToDto(submission);

        // Assert
        Assert.Equal(3, dto.Estimates.Count);
        Assert.Contains(dto.Estimates, e => e.Company == GradingCompany.PSA);
        Assert.Contains(dto.Estimates, e => e.Company == GradingCompany.BGS);
        Assert.Contains(dto.Estimates, e => e.Company == GradingCompany.CGC);
    }

    [Fact]
    public async Task SetImageDerivedScoresAndEstimateAsync_SetsScoresAndTriggersEstimation()
    {
        // Arrange
        var submissionId = Guid.NewGuid();
        var submission = CardSubmission.Create("user-123", Guid.NewGuid());
        var scores = new ConditionScores
        {
            Centering = CenteringMeasurement.Perfect,
            Corners = 8.5,
            Edges = 8.0,
            Surface = 9.0
        };

        var estimates = new List<GradeEstimate>
        {
            GradeEstimate.Create(submissionId, GradingCompany.PSA, 8.0, new Dictionary<string, double>(), 0.75, true, "PSA 8")
        };

        _submissionRepository.GetByIdInternalAsync(submissionId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CardSubmission?>(submission));
        _estimationService.EstimateAllCompaniesAsync(submissionId, Arg.Any<ConditionScores>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(estimates));

        // Act
        await _sut.SetImageDerivedScoresAndEstimateAsync(submissionId, scores);

        // Assert
        Assert.NotNull(submission.ImageDerivedScores);
        Assert.Equal(8.5, submission.ImageDerivedScores.Corners);
        Assert.NotNull(submission.FinalScores);
        await _estimationService.Received(1).EstimateAllCompaniesAsync(submissionId, Arg.Any<ConditionScores>(), Arg.Any<CancellationToken>());
        await _submissionRepository.Received(1).SaveChangesResolvingConcurrencyAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetImageDerivedScoresAndEstimateAsync_WhenManualScoresExist_DoesNotOverrideFinal()
    {
        // Arrange
        var submissionId = Guid.NewGuid();
        var submission = CardSubmission.Create("user-123", Guid.NewGuid());
        var manualScores = new ConditionScores
        {
            Centering = CenteringMeasurement.Perfect,
            Corners = 9.5,
            Edges = 9.5,
            Surface = 9.5
        };
        submission.SetManualScores(manualScores);

        var imageDerivedScores = new ConditionScores
        {
            Centering = CenteringMeasurement.Perfect,
            Corners = 8.0,
            Edges = 8.0,
            Surface = 8.0
        };

        var estimates = new List<GradeEstimate>
        {
            GradeEstimate.Create(submissionId, GradingCompany.PSA, 9.0, new Dictionary<string, double>(), 0.85, true, "PSA 9")
        };

        _submissionRepository.GetByIdInternalAsync(submissionId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CardSubmission?>(submission));
        _estimationService.EstimateAllCompaniesAsync(submissionId, Arg.Any<ConditionScores>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(estimates));

        // Act
        await _sut.SetImageDerivedScoresAndEstimateAsync(submissionId, imageDerivedScores);

        // Assert — manual scores should still be the final scores
        Assert.NotNull(submission.ImageDerivedScores);
        Assert.NotNull(submission.FinalScores);
        Assert.Equal(9.5, submission.FinalScores.Corners);
        await _submissionRepository.Received(1).SaveChangesResolvingConcurrencyAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetImageDerivedScoresAndEstimateAsync_WhenNotFound_ThrowsException()
    {
        // Arrange
        _submissionRepository.GetByIdInternalAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CardSubmission?>(null));

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.SetImageDerivedScoresAndEstimateAsync(Guid.NewGuid(), ConditionScores.Perfect));
        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public async Task SetImageDerivedScoresAndEstimateAsync_PropagatesCancellationToken()
    {
        // Arrange
        var submissionId = Guid.NewGuid();
        var cts = new CancellationTokenSource();
        var submission = CardSubmission.Create("user-123", Guid.NewGuid());

        _submissionRepository.GetByIdInternalAsync(submissionId, cts.Token)
            .Returns(Task.FromResult<CardSubmission?>(submission));
        _estimationService.EstimateAllCompaniesAsync(submissionId, Arg.Any<ConditionScores>(), cts.Token)
            .Returns(Task.FromResult(new List<GradeEstimate>()));

        // Act
        await _sut.SetImageDerivedScoresAndEstimateAsync(submissionId, ConditionScores.Perfect, cts.Token);

        // Assert
        await _submissionRepository.Received(1).GetByIdInternalAsync(submissionId, cts.Token);
        await _submissionRepository.Received(1).SaveChangesResolvingConcurrencyAsync(cts.Token);
    }

    [Fact]
    public void MapToDto_MapsImages()
    {
        // Arrange
        var card = PokemonCard.CreateFromApi(
            "xy1-1", "Pikachu", "XY Base", "xy1", "42", CardRarity.Common, "image.jpg");

        var submission = CardSubmission.Create("user-123", Guid.NewGuid());

        var pokemonCardProperty = typeof(CardSubmission).GetProperty("PokemonCard");
        pokemonCardProperty!.SetValue(submission, card);

        var cardImage = CardImage.Create(submission.Id, "images/front.jpg", "front.jpg", ImageType.Front, 1024);
        submission.AddImage(cardImage);

        _imageStorageService.GetImageUrl("images/front.jpg").Returns("https://storage.test/images/front.jpg");

        // Act
        var dto = _sut.MapToDto(submission);

        // Assert
        Assert.Single(dto.Images);
        Assert.Equal(cardImage.Id, dto.Images[0].Id);
        Assert.Equal("https://storage.test/images/front.jpg", dto.Images[0].ImageUrl);
        Assert.Equal(ImageType.Front, dto.Images[0].ImageType);
        Assert.False(dto.Images[0].IsAnalyzed);
    }

    [Fact]
    public void MapToDto_MapsManualAndImageDerivedScores()
    {
        // Arrange
        var card = PokemonCard.CreateFromApi(
            "xy1-1", "Pikachu", "XY Base", "xy1", "42", CardRarity.Common, null);

        var submission = CardSubmission.Create("user-123", Guid.NewGuid());

        var pokemonCardProperty = typeof(CardSubmission).GetProperty("PokemonCard");
        pokemonCardProperty!.SetValue(submission, card);

        var imageDerivedScores = new ConditionScores
        {
            Centering = CenteringMeasurement.Perfect,
            Corners = 8.0,
            Edges = 8.0,
            Surface = 8.0
        };
        submission.SetImageDerivedScores(imageDerivedScores);

        var manualScores = new ConditionScores
        {
            Centering = CenteringMeasurement.Perfect,
            Corners = 9.0,
            Edges = 9.0,
            Surface = 9.0
        };
        submission.SetManualScores(manualScores);

        // Act
        var dto = _sut.MapToDto(submission);

        // Assert
        Assert.NotNull(dto.ManualScores);
        Assert.Equal(9.0, dto.ManualScores.Corners);
        Assert.NotNull(dto.ImageDerivedScores);
        Assert.Equal(8.0, dto.ImageDerivedScores.Corners);
        Assert.NotNull(dto.FinalScores);
        Assert.Equal(9.0, dto.FinalScores.Corners); // manual takes precedence
    }

    [Fact]
    public void MapToDto_WithAnalyzedImage_SetsIsAnalyzedTrue()
    {
        // Arrange
        var card = PokemonCard.CreateFromApi(
            "xy1-1", "Pikachu", "XY Base", "xy1", "42", CardRarity.Common, null);

        var submission = CardSubmission.Create("user-123", Guid.NewGuid());

        var pokemonCardProperty = typeof(CardSubmission).GetProperty("PokemonCard");
        pokemonCardProperty!.SetValue(submission, card);

        var cardImage = CardImage.Create(submission.Id, "images/front.jpg", "front.jpg", ImageType.Front, 1024);
        cardImage.SetAnalysisResult(new ImageAnalysisResult
        {
            DetectedCentering = CenteringMeasurement.Perfect,
            CornersScore = 9.0,
            EdgesScore = 9.0,
            SurfaceScore = 9.0,
            DetectedDefects = [],
            AnalyzedAt = DateTimeOffset.UtcNow,
            AnalysisMethod = "mock"
        });
        submission.AddImage(cardImage);

        _imageStorageService.GetImageUrl("images/front.jpg").Returns("https://storage.test/images/front.jpg");

        // Act
        var dto = _sut.MapToDto(submission);

        // Assert
        Assert.Single(dto.Images);
        Assert.True(dto.Images[0].IsAnalyzed);
    }
}
