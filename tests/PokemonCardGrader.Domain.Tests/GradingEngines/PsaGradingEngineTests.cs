using PokemonCardGrader.Domain.Enums;
using PokemonCardGrader.Domain.GradingEngines;
using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Domain.Tests.GradingEngines;

public class PsaGradingEngineTests
{
    private readonly PsaGradingEngine _engine = new();

    [Fact]
    public void Company_ReturnsCorrectEnum()
    {
        // Act
        var company = _engine.Company;

        // Assert
        Assert.Equal(GradingCompany.PSA, company);
    }

    [Fact]
    public void EstimateGrade_WithPerfectScores_ReturnsGrade10()
    {
        // Arrange
        var scores = ConditionScores.Perfect;

        // Act
        var result = _engine.EstimateGrade(scores);

        // Assert
        Assert.Equal(10, result.Grade);
        Assert.Equal("Gem Mint", result.Label);
        Assert.Equal(GradingCompany.PSA, result.Company);
        Assert.True(result.IsRuleBased);
    }

    [Fact]
    public void EstimateGrade_WithPerfectScores_HasCorrectSubGrades()
    {
        // Arrange
        var scores = ConditionScores.Perfect;

        // Act
        var result = _engine.EstimateGrade(scores);

        // Assert
        Assert.Equal(10, result.SubGrades["Centering"]);
        Assert.Equal(10, result.SubGrades["Corners"]);
        Assert.Equal(10, result.SubGrades["Edges"]);
        Assert.Equal(10, result.SubGrades["Surface"]);
    }

    [Theory]
    [InlineData(10, "Gem Mint")]
    [InlineData(9, "Mint")]
    [InlineData(8, "NM-MT")]
    [InlineData(7, "Near Mint")]
    [InlineData(6, "EX-MT")]
    [InlineData(5, "Excellent")]
    [InlineData(4, "VG-EX")]
    [InlineData(3, "VG")]
    [InlineData(2, "Good")]
    [InlineData(1, "Poor")]
    public void EstimateGrade_ReturnsCorrectLabelForGrade(int expectedGrade, string expectedLabel)
    {
        // Arrange - adjust scores to target a specific grade
        var centering = new CenteringMeasurement
        {
            LeftRightFront = 50,
            LeftRightBack = 50,
            TopBottomFront = 50,
            TopBottomBack = 50
        };

        var scores = new ConditionScores
        {
            Centering = centering,
            Corners = expectedGrade,
            Edges = expectedGrade,
            Surface = expectedGrade
        };

        // Act
        var result = _engine.EstimateGrade(scores);

        // Assert
        Assert.Equal(expectedGrade, result.Grade);
        Assert.Equal(expectedLabel, result.Label);
    }

    [Fact]
    public void EstimateGrade_UsesMinimumOfAllCategories()
    {
        // Arrange - one category is lower than others
        var centering = new CenteringMeasurement
        {
            LeftRightFront = 50,
            LeftRightBack = 50,
            TopBottomFront = 50,
            TopBottomBack = 50
        };

        var scores = new ConditionScores
        {
            Centering = centering,
            Corners = 10,
            Edges = 10,
            Surface = 6  // Lowest score
        };

        // Act
        var result = _engine.EstimateGrade(scores);

        // Assert
        Assert.Equal(6, result.Grade);
        Assert.Equal("EX-MT", result.Label);
    }

    [Theory]
    [InlineData(55, 70, 10)]  // 55/45 front, 75/25 back = PSA 10
    [InlineData(60, 70, 9)]   // 60/40 front, 75/25 back = PSA 9
    [InlineData(65, 75, 8)]   // 65/35 front, 80/20 back = PSA 8
    [InlineData(70, 80, 7)]   // 70/30 front, 85/15 back = PSA 7
    [InlineData(75, 85, 6)]   // 75/25 front, 90/10 back = PSA 6
    public void EstimateGrade_CenteringThresholds_WorkCorrectly(double frontLarger, double backLarger, int expectedCenteringGrade)
    {
        // Arrange - front centering is off by frontLarger, back by backLarger
        var centering = new CenteringMeasurement
        {
            LeftRightFront = frontLarger,  // This makes larger side = frontLarger
            LeftRightBack = 50,
            TopBottomFront = 50,
            TopBottomBack = backLarger
        };

        var scores = new ConditionScores
        {
            Centering = centering,
            Corners = 10,
            Edges = 10,
            Surface = 10
        };

        // Act
        var result = _engine.EstimateGrade(scores);

        // Assert
        Assert.Equal(expectedCenteringGrade, result.SubGrades["Centering"]);
    }

    [Theory]
    [InlineData(9.5, 10)]
    [InlineData(9.4, 9)]
    [InlineData(8.5, 9)]
    [InlineData(8.4, 8)]
    [InlineData(7.5, 8)]
    [InlineData(1.5, 2)]
    [InlineData(1.4, 1)]
    public void EstimateGrade_MapsDecimalScoresToWholeGrades(double score, int expectedGrade)
    {
        // Arrange
        var scores = new ConditionScores
        {
            Centering = CenteringMeasurement.Perfect,
            Corners = score,
            Edges = 10,
            Surface = 10
        };

        // Act
        var result = _engine.EstimateGrade(scores);

        // Assert
        Assert.Equal(expectedGrade, result.Grade);
    }

    [Fact]
    public void EstimateGrade_WithWorstScores_ReturnsGrade1()
    {
        // Arrange
        var centering = new CenteringMeasurement
        {
            LeftRightFront = 95,  // Severely off-center
            LeftRightBack = 95,
            TopBottomFront = 95,
            TopBottomBack = 95
        };

        var scores = new ConditionScores
        {
            Centering = centering,
            Corners = 1,
            Edges = 1,
            Surface = 1
        };

        // Act
        var result = _engine.EstimateGrade(scores);

        // Assert
        Assert.Equal(1, result.Grade);
        Assert.Equal("Poor", result.Label);
    }

    [Fact]
    public void EstimateGrade_ConfidenceIsConsistent()
    {
        // Arrange
        var scores = ConditionScores.Perfect;

        // Act
        var result = _engine.EstimateGrade(scores);

        // Assert
        Assert.Equal(0.7, result.Confidence);
    }

    [Fact]
    public void EstimateGrade_HasAllFourSubGrades()
    {
        // Arrange
        var scores = ConditionScores.Perfect;

        // Act
        var result = _engine.EstimateGrade(scores);

        // Assert
        Assert.Equal(4, result.SubGrades.Count);
        Assert.Contains("Centering", result.SubGrades.Keys);
        Assert.Contains("Corners", result.SubGrades.Keys);
        Assert.Contains("Edges", result.SubGrades.Keys);
        Assert.Contains("Surface", result.SubGrades.Keys);
    }

    [Fact]
    public void EstimateGrade_CenteringBoundary_55Front75Back_GivesGrade10()
    {
        // Arrange - exact boundary for PSA 10 centering
        var centering = new CenteringMeasurement
        {
            LeftRightFront = 55,
            LeftRightBack = 50,
            TopBottomFront = 50,
            TopBottomBack = 75
        };

        var scores = new ConditionScores
        {
            Centering = centering,
            Corners = 10,
            Edges = 10,
            Surface = 10
        };

        // Act
        var result = _engine.EstimateGrade(scores);

        // Assert
        Assert.Equal(10, result.SubGrades["Centering"]);
        Assert.Equal(10, result.Grade);
    }

    [Fact]
    public void EstimateGrade_BackCenteringPoor_LowersCenteringGrade()
    {
        // Arrange - front perfect, back severely off
        var centering = new CenteringMeasurement
        {
            LeftRightFront = 50,
            LeftRightBack = 50,
            TopBottomFront = 50,
            TopBottomBack = 90  // 90/10 back centering
        };

        var scores = new ConditionScores
        {
            Centering = centering,
            Corners = 10,
            Edges = 10,
            Surface = 10
        };

        // Act
        var result = _engine.EstimateGrade(scores);

        // Assert - back centering should prevent PSA 10
        Assert.True(result.SubGrades["Centering"] < 10);
    }
}
