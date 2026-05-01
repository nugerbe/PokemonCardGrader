using PokemonCardGrader.Domain.Entities;
using PokemonCardGrader.Domain.Enums;

namespace PokemonCardGrader.Domain.Tests.Entities;

public sealed class GradeEstimateTests
{
    [Fact]
    public void Create_SetsAllProperties()
    {
        var submissionId = Guid.NewGuid();
        var subGrades = new Dictionary<string, double> { ["Centering"] = 9.0, ["Corners"] = 9.5 };

        var estimate = GradeEstimate.Create(
            submissionId, GradingCompany.BGS, 9.5, subGrades, 0.92, false, "BGS 9.5");

        Assert.NotEqual(Guid.Empty, estimate.Id);
        Assert.Equal(submissionId, estimate.CardSubmissionId);
        Assert.Equal(GradingCompany.BGS, estimate.Company);
        Assert.Equal(9.5, estimate.PredictedGrade);
        Assert.Equal(subGrades, estimate.SubGrades);
        Assert.Equal(0.92, estimate.Confidence);
        Assert.False(estimate.IsRuleBased);
        Assert.Equal("BGS 9.5", estimate.Label);
        Assert.True(estimate.EstimatedAt <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Create_GeneratesUniqueIds()
    {
        var subId = Guid.NewGuid();
        var e1 = GradeEstimate.Create(subId, GradingCompany.PSA, 9.0, new(), 0.85, true, "PSA 9");
        var e2 = GradeEstimate.Create(subId, GradingCompany.PSA, 9.0, new(), 0.85, true, "PSA 9");

        Assert.NotEqual(e1.Id, e2.Id);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Create_SetsIsRuleBased(bool isRuleBased)
    {
        var estimate = GradeEstimate.Create(
            Guid.NewGuid(), GradingCompany.PSA, 10.0, new(), 0.95, isRuleBased, "PSA 10");

        Assert.Equal(isRuleBased, estimate.IsRuleBased);
    }

    [Fact]
    public void Create_WithEmptySubGrades_Works()
    {
        var estimate = GradeEstimate.Create(
            Guid.NewGuid(), GradingCompany.SGC, 9.0, new Dictionary<string, double>(), 0.80, true, "SGC 9");

        Assert.Empty(estimate.SubGrades);
    }

    [Theory]
    [InlineData(GradingCompany.PSA)]
    [InlineData(GradingCompany.BGS)]
    [InlineData(GradingCompany.CGC)]
    [InlineData(GradingCompany.ACE)]
    [InlineData(GradingCompany.SGC)]
    [InlineData(GradingCompany.TAG)]
    public void Create_AcceptsAllCompanies(GradingCompany company)
    {
        var estimate = GradeEstimate.Create(
            Guid.NewGuid(), company, 9.0, new(), 0.85, true, $"{company} 9");

        Assert.Equal(company, estimate.Company);
    }
}
