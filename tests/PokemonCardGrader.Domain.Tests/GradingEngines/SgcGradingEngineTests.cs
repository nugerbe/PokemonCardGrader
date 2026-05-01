using PokemonCardGrader.Domain.Enums;
using PokemonCardGrader.Domain.GradingEngines;
using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Domain.Tests.GradingEngines;

public class SgcGradingEngineTests
{
    private readonly SgcGradingEngine _engine = new();

    [Fact]
    public void Company_ReturnsCorrectEnum()
    {
        // Act
        var company = _engine.Company;

        // Assert
        Assert.Equal(GradingCompany.SGC, company);
    }

    [Fact]
    public void EstimateGrade_WithAllTens_ReturnsPristineGold()
    {
        // Arrange
        var scores = ConditionScores.Perfect;

        // Act
        var result = _engine.EstimateGrade(scores);

        // Assert
        Assert.Equal(10, result.Grade);
        Assert.Equal("Pristine Gold", result.Label);
        Assert.Equal(GradingCompany.SGC, result.Company);
    }

    [Fact]
    public void EstimateGrade_Grade10WithoutAllTens_ReturnsGemMint()
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
            Corners = 10,            // 10
            Edges = 10,              // 10
            Surface = 9.5            // Not 10
        };
        // Weighted: (10*0.15) + (10*0.25) + (10*0.20) + (9.5*0.40)
        // = 1.5 + 2.5 + 2.0 + 3.8 = 9.8 -> rounds to 10

        // Act
        var result = _engine.EstimateGrade(scores);

        // Assert
        Assert.Equal(10, result.Grade);
        Assert.Equal("Gem Mint", result.Label);  // Not Pristine Gold
    }

    [Fact]
    public void EstimateGrade_WeightedFormula_CalculatesCorrectly()
    {
        // Arrange - Centering(15%) + Corners(25%) + Edges(20%) + Surface(40%)
        var centering = new CenteringMeasurement
        {
            LeftRightFront = 50,
            LeftRightBack = 50,
            TopBottomFront = 50,
            TopBottomBack = 50
        };

        var scores = new ConditionScores
        {
            Centering = centering,  // 10 * 0.15 = 1.5
            Corners = 8,             // 8 * 0.25 = 2.0
            Edges = 9,               // 9 * 0.20 = 1.8
            Surface = 10             // 10 * 0.40 = 4.0
        };
        // Expected: 1.5 + 2.0 + 1.8 + 4.0 = 9.3 -> rounds to 9.5

        // Act
        var result = _engine.EstimateGrade(scores);

        // Assert
        Assert.Equal(9.5, result.Grade);
    }

    [Fact]
    public void EstimateGrade_SurfaceHasHighestWeight()
    {
        // Arrange - surface is low, others are high
        var centering = new CenteringMeasurement
        {
            LeftRightFront = 50,
            LeftRightBack = 50,
            TopBottomFront = 50,
            TopBottomBack = 50
        };

        var scores = new ConditionScores
        {
            Centering = centering,  // 10 * 0.15 = 1.5
            Corners = 10,            // 10 * 0.25 = 2.5
            Edges = 10,              // 10 * 0.20 = 2.0
            Surface = 7              // 7 * 0.40 = 2.8
        };
        // Expected: 1.5 + 2.5 + 2.0 + 2.8 = 8.8 -> rounds to 9.0

        // Act
        var result = _engine.EstimateGrade(scores);

        // Assert
        Assert.Equal(9.0, result.Grade);
    }

    [Theory]
    [InlineData(10, true, "Pristine Gold")]
    [InlineData(10, false, "Pristine Gold")]  // centering 10, all subs 10, isPristine=true
    [InlineData(9.5, false, "Mint+")]         // 10*0.15+9.5*0.85=9.575->9.5="Mint+"
    [InlineData(9.0, false, "Mint")]          // 10*0.15+9*0.85=9.15->9.0="Mint"
    [InlineData(8.5, false, "NM-MT+")]        // 10*0.15+8.5*0.85=8.725->8.5="NM-MT+"
    [InlineData(8.0, false, "NM-MT+")]        // 10*0.15+8*0.85=8.3->8.5="NM-MT+"
    [InlineData(7.0, false, "Near Mint")]     // 10*0.15+7*0.85=7.45->7.5 but rounds to 7.5 which is >=7.0="Near Mint"
    [InlineData(6.0, false, "EX-MT")]         // 10*0.15+6*0.85=6.6->6.5="EX-MT"
    public void EstimateGrade_ReturnsCorrectLabelForGrade(double targetGrade, bool allTens, string expectedLabel)
    {
        // Arrange
        var centering = new CenteringMeasurement
        {
            LeftRightFront = 50,
            LeftRightBack = 50,
            TopBottomFront = 50,
            TopBottomBack = 50
        };

        var scoreValue = allTens ? 10 : targetGrade;
        var scores = new ConditionScores
        {
            Centering = centering,  // Always 10
            Corners = scoreValue,
            Edges = scoreValue,
            Surface = scoreValue
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
    [InlineData(58, 9.0)]    // 58/42 front
    [InlineData(60, 8.5)]    // 60/40 front
    [InlineData(63, 8.0)]    // 63/37 front
    [InlineData(65, 7.5)]    // 65/35 front
    [InlineData(70, 7.0)]    // 70/30 front
    [InlineData(75, 6.0)]    // 75/25 front
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
        // Arrange - SGC: >= 6.0 => "EX-MT", below that => "SGC X.X"
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
            Corners = 4,            // 4
            Edges = 4,              // 4
            Surface = 4             // 4
        };
        // Weighted: (10*0.15) + (4*0.25) + (4*0.20) + (4*0.40) = 1.5 + 1.0 + 0.8 + 1.6 = 4.9 -> 5.0

        // Act
        var result = _engine.EstimateGrade(scores);

        // Assert - grade 5.0 < 6.0 returns formatted "SGC 5.0"
        Assert.StartsWith("SGC", result.Label);
    }

    [Fact]
    public void EstimateGrade_PristineGold_RequiresAllFourTens()
    {
        // Arrange - test that all four must be 10
        var centering = new CenteringMeasurement
        {
            LeftRightFront = 50,
            LeftRightBack = 50,
            TopBottomFront = 50,
            TopBottomBack = 50
        };

        var scoresWithOneNonTen = new ConditionScores
        {
            Centering = centering,
            Corners = 10,
            Edges = 10,
            Surface = 9.5  // One is not 10
        };

        var perfectScores = ConditionScores.Perfect;

        // Act
        var resultWithNonTen = _engine.EstimateGrade(scoresWithOneNonTen);
        var perfectResult = _engine.EstimateGrade(perfectScores);

        // Assert
        Assert.NotEqual("Pristine Gold", resultWithNonTen.Label);
        Assert.Equal("Pristine Gold", perfectResult.Label);
    }

    [Fact]
    public void EstimateGrade_CenteringHasLowestWeight()
    {
        // Arrange - centering is low, others are high
        var centering = new CenteringMeasurement
        {
            LeftRightFront = 75,  // Poor centering (6.0 score)
            LeftRightBack = 50,
            TopBottomFront = 50,
            TopBottomBack = 50
        };

        var scores = new ConditionScores
        {
            Centering = centering,  // 6.0 * 0.15 = 0.9
            Corners = 10,            // 10 * 0.25 = 2.5
            Edges = 10,              // 10 * 0.20 = 2.0
            Surface = 10             // 10 * 0.40 = 4.0
        };
        // Expected: 0.9 + 2.5 + 2.0 + 4.0 = 9.4 -> rounds to 9.5

        // Act
        var result = _engine.EstimateGrade(scores);

        // Assert - despite poor centering, overall grade is still high
        Assert.Equal(9.5, result.Grade);
    }

    [Fact]
    public void EstimateGrade_WeightingFavors_SurfaceAndCorners()
    {
        // Arrange - Surface (40%) + Corners (25%) = 65% of total
        var centering = new CenteringMeasurement
        {
            LeftRightFront = 50,
            LeftRightBack = 50,
            TopBottomFront = 50,
            TopBottomBack = 50
        };

        var scores = new ConditionScores
        {
            Centering = centering,  // 10 * 0.15 = 1.5
            Corners = 6,             // 6 * 0.25 = 1.5
            Edges = 10,              // 10 * 0.20 = 2.0
            Surface = 6              // 6 * 0.40 = 2.4
        };
        // Expected: 1.5 + 1.5 + 2.0 + 2.4 = 7.4 -> rounds to 7.5

        // Act
        var result = _engine.EstimateGrade(scores);

        // Assert
        Assert.Equal(7.5, result.Grade);
    }
}
