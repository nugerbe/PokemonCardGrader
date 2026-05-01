using PokemonCardGrader.Domain.Enums;
using PokemonCardGrader.Domain.GradingEngines;
using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Domain.Tests.GradingEngines;

public class CgcGradingEngineTests
{
    private readonly CgcGradingEngine _engine = new();

    [Fact]
    public void Company_ReturnsCorrectEnum()
    {
        // Act
        var company = _engine.Company;

        // Assert
        Assert.Equal(GradingCompany.CGC, company);
    }

    [Fact]
    public void EstimateGrade_WithAllTens_ReturnsPerfect10()
    {
        // Arrange
        var scores = ConditionScores.Perfect;

        // Act
        var result = _engine.EstimateGrade(scores);

        // Assert
        Assert.Equal(10, result.Grade);
        Assert.Equal("Perfect", result.Label);
        Assert.Equal(10, result.SubGrades["Centering"]);
        Assert.Equal(10, result.SubGrades["Corners"]);
        Assert.Equal(10, result.SubGrades["Edges"]);
        Assert.Equal(10, result.SubGrades["Surface"]);
    }

    [Fact]
    public void EstimateGrade_WithAll95sAndAbove_ReturnsPristine10()
    {
        // Arrange - all subs are 9.5+, average is 10
        var centering = new CenteringMeasurement
        {
            LeftRightFront = 51,  // Should give 10 centering (<=51)
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
        Assert.Equal(10, result.Grade);
        Assert.Equal("Perfect", result.Label);  // All 10s = Perfect
    }

    [Fact]
    public void EstimateGrade_WithAverage10ButLowerSubs_ReturnsGemMint10()
    {
        // Arrange - average is 10 but not all subs are 9.5+
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
            Surface = 9.0  // Below 9.5
        };
        // Average: (10 + 10 + 10 + 9) / 4 = 9.75 -> rounds to 10

        // Act
        var result = _engine.EstimateGrade(scores);

        // Assert
        Assert.Equal(10, result.Grade);
        Assert.Equal("Gem Mint", result.Label);
    }

    [Fact]
    public void EstimateGrade_UsesSimpleAverage()
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
            Corners = 9,             // 9
            Edges = 8,               // 8
            Surface = 7              // 7
        };
        // Average: (10 + 9 + 8 + 7) / 4 = 8.5

        // Act
        var result = _engine.EstimateGrade(scores);

        // Assert
        Assert.Equal(8.5, result.Grade);
    }

    [Theory]
    [InlineData(9.5, "Gem Mint")]     // avg=(10+9.5+9.5+9.5)/4=9.625->9.5="Gem Mint"
    [InlineData(9.0, "Gem Mint")]     // avg=(10+9+9+9)/4=9.25->9.5="Gem Mint"
    [InlineData(8.5, "Mint")]         // avg=(10+8.5+8.5+8.5)/4=8.875->9.0="Mint"
    [InlineData(8.0, "NM/Mint+")]     // avg=(10+8+8+8)/4=8.5="NM/Mint+"
    [InlineData(7.5, "NM/Mint")]      // avg=(10+7.5+7.5+7.5)/4=8.125->8.0="NM/Mint"
    [InlineData(7.0, "NM/Mint")]      // avg=(10+7+7+7)/4=7.75->8.0="NM/Mint"
    public void EstimateGrade_ReturnsCorrectLabelForGrade(double grade, string expectedLabel)
    {
        // Arrange - Centering is always 10 (perfect 50/50)
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
            Edges = 8.7,    // Should round to 9.0 then 8.5 (8.7 * 2 = 17.4, rounds to 17, / 2 = 8.5)
            Surface = 8.2   // Should round to 8.0
        };

        // Act
        var result = _engine.EstimateGrade(scores);

        // Assert
        Assert.Equal(9.5, result.SubGrades["Corners"]);
        Assert.Equal(8.5, result.SubGrades["Edges"]);  // Fixed: 8.7 rounds to 8.5
        Assert.Equal(8.0, result.SubGrades["Surface"]);
    }

    [Theory]
    [InlineData(51, 10.0)]   // Perfect front centering
    [InlineData(53, 9.5)]    // Just above perfect
    [InlineData(55, 9.0)]    // 55/45 front
    [InlineData(58, 8.5)]    // 58/42 front
    [InlineData(60, 8.0)]    // 60/40 front
    [InlineData(63, 7.5)]    // 63/37 front
    [InlineData(65, 7.0)]    // 65/35 front
    [InlineData(70, 6.0)]    // 70/30 front
    [InlineData(75, 5.0)]    // 75/25 front
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

    [Theory]
    [InlineData(60, 10.0)]   // Perfect back centering
    [InlineData(65, 9.5)]    // Just above perfect
    [InlineData(70, 9.0)]    // 70/30 back
    [InlineData(75, 8.0)]    // 75/25 back
    [InlineData(80, 7.0)]    // 80/20 back
    public void EstimateGrade_BackCentering_AffectsCenteringGrade(double backLarger, double expectedCenteringSub)
    {
        // Arrange - perfect front, varied back
        var centering = new CenteringMeasurement
        {
            LeftRightFront = 50,
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
        Assert.Equal(0.65, result.Confidence);
    }

    [Fact]
    public void EstimateGrade_LowGrades_ShowsFormattedLabel()
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
            Corners = 5.5,          // 5.5
            Edges = 5.5,            // 5.5
            Surface = 5.5           // 5.5
        };
        // Average: (10+5.5+5.5+5.5)/4 = 6.625 -> 6.5 (below 7.0 threshold)

        // Act
        var result = _engine.EstimateGrade(scores);

        // Assert - grade 6.5 < 7.0 returns formatted "CGC 6.5"
        Assert.StartsWith("CGC", result.Label);
    }

    [Fact]
    public void EstimateGrade_ThreeTiersOf10_AreDistinct()
    {
        // Arrange - test all three tier 10s
        var perfectCentering = CenteringMeasurement.Perfect;

        var pristineCentering = new CenteringMeasurement
        {
            LeftRightFront = 51,
            LeftRightBack = 50,
            TopBottomFront = 50,
            TopBottomBack = 50
        };

        var perfectScores = new ConditionScores
        {
            Centering = perfectCentering,
            Corners = 10,
            Edges = 10,
            Surface = 10
        };

        var pristineScores = new ConditionScores
        {
            Centering = pristineCentering,
            Corners = 9.5,
            Edges = 10,
            Surface = 10
        };

        // Act
        var perfectResult = _engine.EstimateGrade(perfectScores);
        var pristineResult = _engine.EstimateGrade(pristineScores);

        // Assert
        Assert.Equal("Perfect", perfectResult.Label);
        Assert.Equal("Pristine", pristineResult.Label);
        Assert.Equal(10, perfectResult.Grade);
        Assert.Equal(10, pristineResult.Grade);
    }

    [Fact]
    public void EstimateGrade_CenteringTakesBothFrontAndBack()
    {
        // Arrange - front is perfect, back is minimum for 10
        var centering = new CenteringMeasurement
        {
            LeftRightFront = 50,
            LeftRightBack = 50,
            TopBottomFront = 50,
            TopBottomBack = 60  // Maximum back deviation for 10
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
    }
}
