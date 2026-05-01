using PokemonCardGrader.Domain.Entities;
using PokemonCardGrader.Domain.Enums;

namespace PokemonCardGrader.Domain.Tests.Entities;

public sealed class GradingResultTests
{
    [Fact]
    public void Create_SetsAllProperties()
    {
        var submissionId = Guid.NewGuid();
        var subGrades = new Dictionary<string, double>
        {
            ["Centering"] = 10,
            ["Corners"] = 9.5,
            ["Edges"] = 9.5,
            ["Surface"] = 10
        };

        var result = GradingResult.Create(
            submissionId, "user-123", GradingCompany.PSA, 9.5, subGrades, "PSA12345678");

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal(submissionId, result.CardSubmissionId);
        Assert.Equal("user-123", result.UserId);
        Assert.Equal(GradingCompany.PSA, result.Company);
        Assert.Equal(9.5, result.ActualGrade);
        Assert.Equal(subGrades, result.ActualSubGrades);
        Assert.Equal("PSA12345678", result.CertificationNumber);
        Assert.True(result.RecordedAt <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Create_WithNullCertificationNumber_Works()
    {
        var result = GradingResult.Create(
            Guid.NewGuid(), "user", GradingCompany.BGS, 9.0, new(), null);

        Assert.Null(result.CertificationNumber);
    }

    [Fact]
    public void Create_GeneratesUniqueIds()
    {
        var r1 = GradingResult.Create(Guid.NewGuid(), "u", GradingCompany.PSA, 10, new(), null);
        var r2 = GradingResult.Create(Guid.NewGuid(), "u", GradingCompany.PSA, 10, new(), null);

        Assert.NotEqual(r1.Id, r2.Id);
    }

    [Fact]
    public void Create_WithEmptySubGrades_Works()
    {
        var result = GradingResult.Create(
            Guid.NewGuid(), "user", GradingCompany.SGC, 9.0, new Dictionary<string, double>(), null);

        Assert.Empty(result.ActualSubGrades);
    }
}
