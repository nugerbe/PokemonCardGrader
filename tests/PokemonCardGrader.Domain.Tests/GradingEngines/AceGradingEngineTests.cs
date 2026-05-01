using PokemonCardGrader.Domain.Enums;
using PokemonCardGrader.Domain.GradingEngines;
using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Domain.Tests.GradingEngines;

public class AceGradingEngineTests
{
    private readonly AceGradingEngine _engine = new();

    [Fact]
    public void Company_ReturnsCorrectEnum()
    {
        // Act
        var company = _engine.Company;

        // Assert
        Assert.Equal(GradingCompany.ACE, company);
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
        Assert.Equal(GradingCompany.ACE, result.Company);
    }

    [Fact]
    public void EstimateGrade_CapsAtLowestSubPlusOne()
    {
        // Arrange - one sub is much lower than others
        var centering = new CenteringMeasurement
        {
            LeftRightFront = 50,
            LeftRightBack = 50,
            TopBottomFront = 50,
            TopBottomBack = 50
        };

        var scores = new ConditionScores
        {
            Centering = centering,  // 10
            Corners = 10,            // 10
            Edges = 10,              // 10
            Surface = 8              // Lowest = 8
        };
        // Average: (10 + 10 + 10 + 8) / 4 = 9.5
        // But ACE caps at lowest + 1 = 8 + 1 = 9

        // Act
        var result = _engine.EstimateGrade(scores);

        // Assert
        Assert.Equal(9.0, result.Grade);
    }

    [Fact]
    public void EstimateGrade_LowestSubPlus1Rule_WorksCorrectly()
    {
        // Arrange
        var centering = new CenteringMeasurement
        {
            LeftRightFront = 50,
            LeftRightBack = 50,
            TopBottomFront = 50,
            TopBottomBack = 50
        };

        var scores = new ConditionScores
        {
            Centering = centering,  // 10
            Corners = 9.5,           // 9.5
            Edges = 9.5,             // 9.5
            Surface = 7.5            // Lowest = 7.5
        };
        // Average: (10 + 9.5 + 9.5 + 7.5) / 4 = 9.125 -> rounds to 9.0
        // Cap: 7.5 + 1 = 8.5
        // Final: min(9.0, 8.5) = 8.5

        // Act
        var result = _engine.EstimateGrade(scores);

        // Assert
        Assert.Equal(8.5, result.Grade);
    }

    [Fact]
    public void EstimateGrade_CapDoesNotExceed10()
    {
        // Arrange - lowest sub is 9.5, so cap would be 10.5, but max is 10
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
            Surface = 9.5  // Lowest = 9.5
        };
        // Average: (10 + 10 + 10 + 9.5) / 4 = 9.875 -> rounds to 10
        // Cap: 9.5 + 1 = 10.5, but max is 10
        // Final: min(10, 10) = 10

        // Act
        var result = _engine.EstimateGrade(scores);

        // Assert
        Assert.Equal(10, result.Grade);
    }

    [Theory]
    [InlineData(10, "Gem Mint")]    // avg=10, cap=11->10, grade=10="Gem Mint"
    [InlineData(9.5, "Mint+")]      // avg=9.625->9.5, cap=10.5->10, grade=9.5="Mint+"
    [InlineData(9.0, "Mint+")]      // avg=9.25->9.5, cap=10, grade=9.5="Mint+"
    [InlineData(8.5, "Mint")]       // avg=8.875->9.0, cap=9.5, grade=9.0="Mint"
    [InlineData(8.0, "NM-MT+")]     // avg=8.5, cap=9, grade=8.5="NM-MT+"
    [InlineData(7.0, "NM-MT")]      // avg=7.75->8.0, cap=8, grade=8.0="NM-MT"
    public void EstimateGrade_ReturnsCorrectLabelForGrade(double grade, string expectedLabel)
    {
        // Arrange
        var centering = new CenteringMeasurement
        {
            LeftRightFront = 50,
            LeftRightBack = 50,
            TopBottomFront = 50,
            TopBottomBack = 50
        };

        var scores = new ConditionScores
        {
            Centering = centering,  // Always 10
            Corners = grade,
            Edges = grade,
            Surface = grade
        };

        // Act
        var result = _engine.EstimateGrade(scores);

        // Assert
        Assert.Equal(expectedLabel, result.Label);
    }

    [Fact]
    public void EstimateGrade_RoundsToHalfIncrements()
    {
        // Arrange
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
            Corners = 9.3,  // Should round to 9.5
            Edges = 8.7,    // Should round to 8.5 (MidpointRounding.AwayFromZero)
            Surface = 8.2   // Should round to 8.0
        };

        // Act
        var result = _engine.EstimateGrade(scores);

        // Assert
        Assert.Equal(9.5, result.SubGrades["Corners"]);
        Assert.Equal(8.5, result.SubGrades["Edges"]);
        Assert.Equal(8.0, result.SubGrades["Surface"]);
    }

    [Theory]
    [InlineData(52, 10.0)]   // Perfect front centering
    [InlineData(55, 9.5)]    // Just above perfect
    [InlineData(57, 9.0)]    // 57/43 front
    [InlineData(60, 8.5)]    // 60/40 front
    [InlineData(63, 8.0)]    // 63/37 front
    [InlineData(65, 7.5)]    // 65/35 front
    [InlineData(68, 7.0)]    // 68/32 front
    [InlineData(72, 6.0)]    // 72/28 front
    [InlineData(78, 5.0)]    // 78/22 front
    public void EstimateGrade_CenteringThresholds_WorkCorrectly(double frontLarger, double expectedCenteringSub)
    {
        // Arrange
        var centering = new CenteringMeasurement
        {
            LeftRightFront = frontLarger,
            LeftRightBack = 50,
            TopBottomFront = 50,
            TopBottomBack = 50
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
        Assert.Equal(expectedCenteringSub, result.SubGrades["Centering"]);
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
    public void EstimateGrade_IsRuleBased_IsTrue()
    {
        // Arrange
        var scores = ConditionScores.Perfect;

        // Act
        var result = _engine.EstimateGrade(scores);

        // Assert
        Assert.True(result.IsRuleBased);
    }

    [Fact]
    public void EstimateGrade_ConfidenceIsConsistent()
    {
        // Arrange
        var scores = ConditionScores.Perfect;

        // Act
        var result = _engine.EstimateGrade(scores);

        // Assert
        Assert.Equal(0.6, result.Confidence);
    }

    [Fact]
    public void EstimateGrade_LowGrades_ShowsFormattedLabel()
    {
        // Arrange - ACE: >= 7.0 => "Near Mint", below that => "ACE X.X"
        var centering = new CenteringMeasurement
        {
            LeftRightFront = 50,
            LeftRightBack = 50,
            TopBottomFront = 50,
            TopBottomBack = 50
        };

        var scores = new ConditionScores
        {
            Centering = centering,  // 10
            Corners = 5.5,          // 5.5
            Edges = 5.5,            // 5.5
            Surface = 5.5           // 5.5
        };
        // Average: (10+5.5+5.5+5.5)/4 = 6.625 -> 6.5, cap: 5.5+1 = 6.5, min(6.5,6.5) = 6.5

        // Act
        var result = _engine.EstimateGrade(scores);

        // Assert - grade 6.5 < 7.0 returns formatted "ACE 6.5"
        Assert.StartsWith("ACE", result.Label);
    }

    [Fact]
    public void EstimateGrade_OneVeryLowSub_SeverelyLimitsGrade()
    {
        // Arrange - most subs are 10, but one is 5
        var centering = new CenteringMeasurement
        {
            LeftRightFront = 50,
            LeftRightBack = 50,
            TopBottomFront = 50,
            TopBottomBack = 50
        };

        var scores = new ConditionScores
        {
            Centering = centering,  // 10
            Corners = 10,            // 10
            Edges = 10,              // 10
            Surface = 5              // Lowest = 5
        };
        // Average: (10 + 10 + 10 + 5) / 4 = 8.75 -> rounds to 9.0
        // Cap: 5 + 1 = 6
        // Final: min(9.0, 6) = 6

        // Act
        var result = _engine.EstimateGrade(scores);

        // Assert
        Assert.Equal(6.0, result.Grade);
    }

    [Fact]
    public void EstimateGrade_WithoutCap_AverageWouldBeHigher()
    {
        // Arrange - demonstrate that cap actually limits the grade
        var centering = new CenteringMeasurement
        {
            LeftRightFront = 50,
            LeftRightBack = 50,
            TopBottomFront = 50,
            TopBottomBack = 50
        };

        var scores = new ConditionScores
        {
            Centering = centering,  // 10
            Corners = 10,            // 10
            Edges = 10,              // 10
            Surface = 7.5            // Lowest = 7.5
        };
        // Average: (10 + 10 + 10 + 7.5) / 4 = 9.375 -> would round to 9.5
        // Cap: 7.5 + 1 = 8.5
        // Final: min(9.5, 8.5) = 8.5 (cap is active)

        // Act
        var result = _engine.EstimateGrade(scores);

        // Assert
        Assert.Equal(8.5, result.Grade);
        Assert.True(result.SubGrades["Surface"] < result.Grade);  // Surface is lower than overall
    }

    [Fact]
    public void EstimateGrade_AllEqualScores_NoCapEffect()
    {
        // Arrange - all scores equal, so cap shouldn't affect
        var centering = new CenteringMeasurement
        {
            LeftRightFront = 50,
            LeftRightBack = 50,
            TopBottomFront = 50,
            TopBottomBack = 50
        };

        var scores = new ConditionScores
        {
            Centering = centering,  // 10
            Corners = 8.5,          // 8.5
            Edges = 8.5,            // 8.5
            Surface = 8.5           // 8.5
        };
        // Average: (10+8.5+8.5+8.5)/4 = 8.875 -> rounds to 9.0
        // Cap: 8.5 + 1 = 9.5
        // Final: min(9.0, 9.5) = 9.0

        // Act
        var result = _engine.EstimateGrade(scores);

        // Assert
        Assert.Equal(9.0, result.Grade);
    }
}
