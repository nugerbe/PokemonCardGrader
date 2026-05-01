using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PokemonCardGrader.Application.Interfaces;
using PokemonCardGrader.Application.Services;
using PokemonCardGrader.Domain.Entities;
using PokemonCardGrader.Domain.Enums;
using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Application.Tests.Services;

public sealed class DashboardServiceTests
{
    private readonly ICardSubmissionRepository _submissionRepository;
    private readonly CardSubmissionService _submissionService;
    private readonly DashboardService _sut;

    public DashboardServiceTests()
    {
        _submissionRepository = Substitute.For<ICardSubmissionRepository>();
        var estimationService = Substitute.For<IGradeEstimationService>();
        var gradingResultRepository = Substitute.For<IGradingResultRepository>();
        var imageStorageService = Substitute.For<IImageStorageService>();
        var imageAnalysisService = Substitute.For<IImageAnalysisService>();
        _submissionService = new CardSubmissionService(
            _submissionRepository, estimationService, gradingResultRepository, imageStorageService,
            imageAnalysisService, NullLogger<CardSubmissionService>.Instance);
        _sut = new DashboardService(_submissionRepository, _submissionService);
    }

    [Fact]
    public async Task GetDashboardAsync_ReturnsCompleteData()
    {
        // Arrange
        var userId = "user-123";
        var cardId = Guid.NewGuid();
        var card = PokemonCard.CreateFromApi("xy1-1", "Pikachu", "XY Base", "xy1", "42", CardRarity.Common, null);

        var submission1 = CardSubmission.Create(userId, cardId);
        var submission2 = CardSubmission.Create(userId, cardId);

        // Set PokemonCard property using reflection
        var pokemonCardProperty = typeof(CardSubmission).GetProperty("PokemonCard");
        pokemonCardProperty!.SetValue(submission1, card);
        pokemonCardProperty!.SetValue(submission2, card);

        var estimate1 = GradeEstimate.Create(
            submission1.Id, GradingCompany.PSA, 9.0,
            new Dictionary<string, double>(), 0.85, true, "PSA 9");
        var estimate2 = GradeEstimate.Create(
            submission2.Id, GradingCompany.PSA, 10.0,
            new Dictionary<string, double>(), 0.95, true, "PSA 10");

        submission1.SetEstimates(new[] { estimate1 });
        submission2.SetEstimates(new[] { estimate2 });

        var result = GradingResult.Create(
            submission1.Id, userId, GradingCompany.PSA, 9.0,
            new Dictionary<string, double>(), "CERT123");
        submission1.RecordActualResult(result);

        _submissionRepository.GetCountByUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(10));
        _submissionRepository.GetRecentByUserIdAsync(userId, 5, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<CardSubmission> { submission1, submission2 }));

        // Act
        var dashboard = await _sut.GetDashboardAsync(userId);

        // Assert
        Assert.NotNull(dashboard);
        Assert.Equal(10, dashboard.TotalSubmissions);
        Assert.Equal(1, dashboard.GradedCards);
        Assert.Equal(2, dashboard.RecentSubmissions.Count);
        Assert.NotNull(dashboard.AverageEstimatedGrade);
        Assert.Equal(9.5, dashboard.AverageEstimatedGrade.Value);
    }

    [Fact]
    public async Task GetDashboardAsync_WithNoSubmissions_ReturnsEmptyData()
    {
        // Arrange
        var userId = "user-123";

        _submissionRepository.GetCountByUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(0));
        _submissionRepository.GetRecentByUserIdAsync(userId, 5, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<CardSubmission>()));

        // Act
        var dashboard = await _sut.GetDashboardAsync(userId);

        // Assert
        Assert.NotNull(dashboard);
        Assert.Equal(0, dashboard.TotalSubmissions);
        Assert.Equal(0, dashboard.GradedCards);
        Assert.Empty(dashboard.RecentSubmissions);
        Assert.Empty(dashboard.GradeDistribution);
        Assert.Null(dashboard.AverageEstimatedGrade);
    }

    [Fact]
    public async Task GetDashboardAsync_CountsGradedCardsCorrectly()
    {
        // Arrange
        var userId = "user-123";
        var card = PokemonCard.CreateFromApi("xy1-1", "Pikachu", "XY", "xy1", "1", CardRarity.Common, null);

        var submission1 = CardSubmission.Create(userId, Guid.NewGuid());
        var submission2 = CardSubmission.Create(userId, Guid.NewGuid());
        var submission3 = CardSubmission.Create(userId, Guid.NewGuid());

        var pokemonCardProperty = typeof(CardSubmission).GetProperty("PokemonCard");
        pokemonCardProperty!.SetValue(submission1, card);
        pokemonCardProperty!.SetValue(submission2, card);
        pokemonCardProperty!.SetValue(submission3, card);

        // Only submission1 has actual result
        var result = GradingResult.Create(
            submission1.Id, userId, GradingCompany.PSA, 10.0,
            new Dictionary<string, double>(), null);
        submission1.RecordActualResult(result);

        _submissionRepository.GetCountByUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(3));
        _submissionRepository.GetRecentByUserIdAsync(userId, 5, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<CardSubmission> { submission1, submission2, submission3 }));

        // Act
        var dashboard = await _sut.GetDashboardAsync(userId);

        // Assert
        Assert.Equal(1, dashboard.GradedCards);
    }

    [Fact]
    public async Task GetDashboardAsync_CalculatesGradeDistribution()
    {
        // Arrange
        var userId = "user-123";
        var card = PokemonCard.CreateFromApi("xy1-1", "Test", "Set", "xy1", "1", CardRarity.Common, null);

        var submissions = new List<CardSubmission>();
        var expectedDistribution = new Dictionary<string, int>();

        // Create submissions with different grades
        var grades = new[] { 9.7, 9.3, 8.0, 6.8, 5.0 };
        var expectedBuckets = new[] { "10", "9", "8", "7", "6 or below" };

        for (int i = 0; i < grades.Length; i++)
        {
            var submission = CardSubmission.Create(userId, Guid.NewGuid());
            var pokemonCardProperty = typeof(CardSubmission).GetProperty("PokemonCard");
            pokemonCardProperty!.SetValue(submission, card);

            var estimate = GradeEstimate.Create(
                submission.Id, GradingCompany.PSA, grades[i],
                new Dictionary<string, double>(), 0.85, true, $"PSA {grades[i]}");
            submission.SetEstimates(new[] { estimate });

            submissions.Add(submission);

            var bucket = expectedBuckets[i];
            expectedDistribution[bucket] = expectedDistribution.GetValueOrDefault(bucket) + 1;
        }

        _submissionRepository.GetCountByUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(submissions.Count));
        _submissionRepository.GetRecentByUserIdAsync(userId, 5, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(submissions));

        // Act
        var dashboard = await _sut.GetDashboardAsync(userId);

        // Assert
        Assert.NotNull(dashboard.GradeDistribution);
        Assert.Equal(expectedDistribution.Count, dashboard.GradeDistribution.Count);
        foreach (var kvp in expectedDistribution)
        {
            Assert.Equal(kvp.Value, dashboard.GradeDistribution[kvp.Key]);
        }
    }

    [Theory]
    [InlineData(9.7, "10")]
    [InlineData(9.5, "10")]
    [InlineData(9.4, "9")]
    [InlineData(8.9, "9")]
    [InlineData(8.5, "9")]
    [InlineData(8.4, "8")]
    [InlineData(7.9, "8")]
    [InlineData(7.5, "8")]
    [InlineData(7.4, "7")]
    [InlineData(6.9, "7")]
    [InlineData(6.5, "7")]
    [InlineData(6.4, "6 or below")]
    [InlineData(5.0, "6 or below")]
    [InlineData(1.0, "6 or below")]
    public async Task GetDashboardAsync_BucketsGradesCorrectly(double grade, string expectedBucket)
    {
        // Arrange
        var userId = "user-123";
        var card = PokemonCard.CreateFromApi("xy1-1", "Test", "Set", "xy1", "1", CardRarity.Common, null);
        var submission = CardSubmission.Create(userId, Guid.NewGuid());

        var pokemonCardProperty = typeof(CardSubmission).GetProperty("PokemonCard");
        pokemonCardProperty!.SetValue(submission, card);

        var estimate = GradeEstimate.Create(
            submission.Id, GradingCompany.PSA, grade,
            new Dictionary<string, double>(), 0.85, true, $"PSA {grade}");
        submission.SetEstimates(new[] { estimate });

        _submissionRepository.GetCountByUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(1));
        _submissionRepository.GetRecentByUserIdAsync(userId, 5, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<CardSubmission> { submission }));

        // Act
        var dashboard = await _sut.GetDashboardAsync(userId);

        // Assert
        Assert.Single(dashboard.GradeDistribution);
        Assert.True(dashboard.GradeDistribution.ContainsKey(expectedBucket));
        Assert.Equal(1, dashboard.GradeDistribution[expectedBucket]);
    }

    [Fact]
    public async Task GetDashboardAsync_OnlyCountsPSAEstimates()
    {
        // Arrange
        var userId = "user-123";
        var card = PokemonCard.CreateFromApi("xy1-1", "Test", "Set", "xy1", "1", CardRarity.Common, null);
        var submission = CardSubmission.Create(userId, Guid.NewGuid());

        var pokemonCardProperty = typeof(CardSubmission).GetProperty("PokemonCard");
        pokemonCardProperty!.SetValue(submission, card);

        var estimates = new[]
        {
            GradeEstimate.Create(submission.Id, GradingCompany.PSA, 9.5, new Dictionary<string, double>(), 0.85, true, "PSA 9.5"),
            GradeEstimate.Create(submission.Id, GradingCompany.BGS, 9.0, new Dictionary<string, double>(), 0.80, false, "BGS 9"),
            GradeEstimate.Create(submission.Id, GradingCompany.CGC, 8.5, new Dictionary<string, double>(), 0.75, true, "CGC 8.5")
        };
        submission.SetEstimates(estimates);

        _submissionRepository.GetCountByUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(1));
        _submissionRepository.GetRecentByUserIdAsync(userId, 5, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<CardSubmission> { submission }));

        // Act
        var dashboard = await _sut.GetDashboardAsync(userId);

        // Assert
        Assert.Single(dashboard.GradeDistribution);
        Assert.True(dashboard.GradeDistribution.ContainsKey("10"));
        Assert.Equal(9.5, dashboard.AverageEstimatedGrade);
    }

    [Fact]
    public async Task GetDashboardAsync_WithNoEstimates_HasNullAverage()
    {
        // Arrange
        var userId = "user-123";
        var card = PokemonCard.CreateFromApi("xy1-1", "Test", "Set", "xy1", "1", CardRarity.Common, null);
        var submission = CardSubmission.Create(userId, Guid.NewGuid());

        var pokemonCardProperty = typeof(CardSubmission).GetProperty("PokemonCard");
        pokemonCardProperty!.SetValue(submission, card);

        _submissionRepository.GetCountByUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(1));
        _submissionRepository.GetRecentByUserIdAsync(userId, 5, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<CardSubmission> { submission }));

        // Act
        var dashboard = await _sut.GetDashboardAsync(userId);

        // Assert
        Assert.Null(dashboard.AverageEstimatedGrade);
        Assert.Empty(dashboard.GradeDistribution);
    }

    [Fact]
    public async Task GetDashboardAsync_PropagatesCancellationToken()
    {
        // Arrange
        var userId = "user-123";
        var cts = new CancellationTokenSource();

        _submissionRepository.GetCountByUserIdAsync(userId, cts.Token)
            .Returns(Task.FromResult(0));
        _submissionRepository.GetRecentByUserIdAsync(userId, 5, cts.Token)
            .Returns(Task.FromResult(new List<CardSubmission>()));

        // Act
        await _sut.GetDashboardAsync(userId, cts.Token);

        // Assert
        await _submissionRepository.Received(1).GetCountByUserIdAsync(userId, cts.Token);
        await _submissionRepository.Received(1).GetRecentByUserIdAsync(userId, 5, cts.Token);
    }

    [Fact]
    public async Task GetDashboardAsync_MapsRecentSubmissionsCorrectly()
    {
        // Arrange
        var userId = "user-123";
        var card = PokemonCard.CreateFromApi("xy1-1", "Pikachu", "XY Base", "xy1", "42", CardRarity.RareHolo, "image.jpg");

        var submission = CardSubmission.Create(userId, Guid.NewGuid());
        var pokemonCardProperty = typeof(CardSubmission).GetProperty("PokemonCard");
        pokemonCardProperty!.SetValue(submission, card);

        var estimate = GradeEstimate.Create(
            submission.Id, GradingCompany.PSA, 9.5,
            new Dictionary<string, double> { ["Overall"] = 9.5 }, 0.90, true, "PSA 9.5");
        submission.SetEstimates(new[] { estimate });

        _submissionRepository.GetCountByUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(1));
        _submissionRepository.GetRecentByUserIdAsync(userId, 5, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<CardSubmission> { submission }));

        // Act
        var dashboard = await _sut.GetDashboardAsync(userId);

        // Assert
        Assert.Single(dashboard.RecentSubmissions);
        var dto = dashboard.RecentSubmissions[0];
        Assert.Equal(submission.Id, dto.Id);
        Assert.Equal("Pikachu", dto.CardName);
        Assert.Equal("XY Base", dto.SetName);
        Assert.Equal("42", dto.Number);
        Assert.Equal("image.jpg", dto.CardImageUrl);
        Assert.Single(dto.Estimates);
        Assert.Equal(9.5, dto.Estimates[0].PredictedGrade);
    }

    [Fact]
    public async Task GetDashboardAsync_WithMixedGrades_CalculatesCorrectAverage()
    {
        // Arrange
        var userId = "user-123";
        var card = PokemonCard.CreateFromApi("xy1-1", "Test", "Set", "xy1", "1", CardRarity.Common, null);

        var submission1 = CardSubmission.Create(userId, Guid.NewGuid());
        var submission2 = CardSubmission.Create(userId, Guid.NewGuid());
        var submission3 = CardSubmission.Create(userId, Guid.NewGuid());

        var pokemonCardProperty = typeof(CardSubmission).GetProperty("PokemonCard");
        pokemonCardProperty!.SetValue(submission1, card);
        pokemonCardProperty!.SetValue(submission2, card);
        pokemonCardProperty!.SetValue(submission3, card);

        submission1.SetEstimates(new[]
        {
            GradeEstimate.Create(submission1.Id, GradingCompany.PSA, 10.0, new Dictionary<string, double>(), 0.95, true, "PSA 10")
        });
        submission2.SetEstimates(new[]
        {
            GradeEstimate.Create(submission2.Id, GradingCompany.PSA, 9.0, new Dictionary<string, double>(), 0.85, true, "PSA 9")
        });
        submission3.SetEstimates(new[]
        {
            GradeEstimate.Create(submission3.Id, GradingCompany.PSA, 8.0, new Dictionary<string, double>(), 0.75, true, "PSA 8")
        });

        _submissionRepository.GetCountByUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(3));
        _submissionRepository.GetRecentByUserIdAsync(userId, 5, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<CardSubmission> { submission1, submission2, submission3 }));

        // Act
        var dashboard = await _sut.GetDashboardAsync(userId);

        // Assert
        Assert.Equal(9.0, dashboard.AverageEstimatedGrade);
    }
}
