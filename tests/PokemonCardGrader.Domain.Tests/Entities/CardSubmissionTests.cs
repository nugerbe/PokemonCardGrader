using PokemonCardGrader.Domain.Entities;
using PokemonCardGrader.Domain.Enums;
using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Domain.Tests.Entities;

public sealed class CardSubmissionTests
{
    [Fact]
    public void Create_SetsRequiredProperties()
    {
        var userId = "user-123";
        var cardId = Guid.NewGuid();

        var submission = CardSubmission.Create(userId, cardId);

        Assert.NotEqual(Guid.Empty, submission.Id);
        Assert.Equal(userId, submission.UserId);
        Assert.Equal(cardId, submission.PokemonCardId);
        Assert.Null(submission.Notes);
        Assert.Null(submission.ManualScores);
        Assert.Null(submission.ImageDerivedScores);
        Assert.Null(submission.FinalScores);
        Assert.Null(submission.ActualResult);
        Assert.Empty(submission.Images);
        Assert.Empty(submission.Estimates);
        Assert.True(submission.CreatedAt <= DateTimeOffset.UtcNow);
        Assert.Equal(submission.CreatedAt, submission.UpdatedAt);
    }

    [Fact]
    public void Create_WithNotes_SetsNotes()
    {
        var submission = CardSubmission.Create("user", Guid.NewGuid(), "My notes");

        Assert.Equal("My notes", submission.Notes);
    }

    [Fact]
    public void Create_GeneratesUniqueIds()
    {
        var s1 = CardSubmission.Create("user", Guid.NewGuid());
        var s2 = CardSubmission.Create("user", Guid.NewGuid());

        Assert.NotEqual(s1.Id, s2.Id);
    }

    [Fact]
    public void SetManualScores_UpdatesManualAndFinalScores()
    {
        var submission = CardSubmission.Create("user", Guid.NewGuid());
        var scores = ConditionScores.Perfect;

        submission.SetManualScores(scores);

        Assert.Equal(scores, submission.ManualScores);
        Assert.Equal(scores, submission.FinalScores);
    }

    [Fact]
    public void SetManualScores_UpdatesTimestamp()
    {
        var submission = CardSubmission.Create("user", Guid.NewGuid());
        var originalUpdated = submission.UpdatedAt;

        submission.SetManualScores(ConditionScores.Perfect);

        Assert.True(submission.UpdatedAt >= originalUpdated);
    }

    [Fact]
    public void SetImageDerivedScores_SetsFinalScores_WhenNoManualScores()
    {
        var submission = CardSubmission.Create("user", Guid.NewGuid());
        var imageScores = new ConditionScores
        {
            Centering = CenteringMeasurement.Perfect,
            Corners = 8.0,
            Edges = 8.5,
            Surface = 9.0
        };

        submission.SetImageDerivedScores(imageScores);

        Assert.Equal(imageScores, submission.ImageDerivedScores);
        Assert.Equal(imageScores, submission.FinalScores);
    }

    [Fact]
    public void SetImageDerivedScores_DoesNotOverrideManualScores()
    {
        var submission = CardSubmission.Create("user", Guid.NewGuid());
        var manualScores = ConditionScores.Perfect;
        var imageScores = new ConditionScores
        {
            Centering = CenteringMeasurement.Perfect,
            Corners = 7.0,
            Edges = 7.0,
            Surface = 7.0
        };

        submission.SetManualScores(manualScores);
        submission.SetImageDerivedScores(imageScores);

        Assert.Equal(imageScores, submission.ImageDerivedScores);
        Assert.Equal(manualScores, submission.FinalScores);
    }

    [Fact]
    public void SetFinalScores_OverridesAnyPreviousScores()
    {
        var submission = CardSubmission.Create("user", Guid.NewGuid());
        submission.SetManualScores(ConditionScores.Perfect);

        var overrideScores = new ConditionScores
        {
            Centering = CenteringMeasurement.Perfect,
            Corners = 5.0,
            Edges = 5.0,
            Surface = 5.0
        };
        submission.SetFinalScores(overrideScores);

        Assert.Equal(overrideScores, submission.FinalScores);
    }

    [Fact]
    public void AddImage_AddsToImagesList()
    {
        var submission = CardSubmission.Create("user", Guid.NewGuid());
        var image = CardImage.Create(submission.Id, "path/img.jpg", "img.jpg", ImageType.Front, 1024);

        submission.AddImage(image);

        Assert.Single(submission.Images);
        Assert.Equal(image, submission.Images[0]);
    }

    [Fact]
    public void AddImage_AllowsMultipleImages()
    {
        var submission = CardSubmission.Create("user", Guid.NewGuid());
        var front = CardImage.Create(submission.Id, "path/front.jpg", "front.jpg", ImageType.Front, 1024);
        var back = CardImage.Create(submission.Id, "path/back.jpg", "back.jpg", ImageType.Back, 2048);

        submission.AddImage(front);
        submission.AddImage(back);

        Assert.Equal(2, submission.Images.Count);
    }

    [Fact]
    public void SetEstimates_ReplacesExistingEstimates()
    {
        var submission = CardSubmission.Create("user", Guid.NewGuid());
        var estimate1 = GradeEstimate.Create(
            submission.Id, GradingCompany.PSA, 9.0, new Dictionary<string, double>(), 0.85, true, "PSA 9");

        submission.SetEstimates(new[] { estimate1 });
        Assert.Single(submission.Estimates);

        var estimate2 = GradeEstimate.Create(
            submission.Id, GradingCompany.BGS, 9.5, new Dictionary<string, double>(), 0.90, false, "BGS 9.5");
        submission.SetEstimates(new[] { estimate2 });

        Assert.Single(submission.Estimates);
        Assert.Equal(GradingCompany.BGS, submission.Estimates[0].Company);
    }

    [Fact]
    public void RecordActualResult_SetsResult()
    {
        var submission = CardSubmission.Create("user", Guid.NewGuid());
        var result = GradingResult.Create(
            submission.Id, "user", GradingCompany.PSA, 10.0, new Dictionary<string, double>(), "CERT123");

        submission.RecordActualResult(result);

        Assert.NotNull(submission.ActualResult);
        Assert.Equal(GradingCompany.PSA, submission.ActualResult.Company);
        Assert.Equal(10.0, submission.ActualResult.ActualGrade);
        Assert.Equal("CERT123", submission.ActualResult.CertificationNumber);
    }
}
