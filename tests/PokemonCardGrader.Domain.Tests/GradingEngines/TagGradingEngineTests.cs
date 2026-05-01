using PokemonCardGrader.Domain.Enums;
using PokemonCardGrader.Domain.GradingEngines;
using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Domain.Tests.GradingEngines;

public class TagGradingEngineTests
{
    private readonly TagGradingEngine _engine = new();

    [Fact]
    public void Company_ReturnsCorrectEnum()
    {
        // Act
        var company = _engine.Company;

        // Assert
        Assert.Equal(GradingCompany.TAG, company);
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
        Assert.Equal(GradingCompany.TAG, result.Company);
    }

    [Fact]
    public void EstimateGrade_Uses1000PointSystem()
    {
        // Arrange - 5 categories, each worth 200 points max
        // Perfect score: 10 in each category = 200 points each = 1000 total
        // Total / 100 = 10
        var scores = ConditionScores.Perfect;

        // Act
        var result = _engine.EstimateGrade(scores);

        // Assert
        Assert.Equal(10, result.Grade);
    }

    [Fact]
    public void EstimateGrade_HasFiveSubGrades()
    {
        // Arrange
        var scores = ConditionScores.Perfect;

        // Act
        var result = _engine.EstimateGrade(scores);

        // Assert
        Assert.Equal(5, result.SubGrades.Count);
        Assert.Contains("Centering", result.SubGrades.Keys);
        Assert.Contains("Corners", result.SubGrades.Keys);
        Assert.Contains("Edges", result.SubGrades.Keys);
        Assert.Contains("Surface", result.SubGrades.Keys);
        Assert.Contains("Dimensions", result.SubGrades.Keys);
    }

    [Fact]
    public void EstimateGrade_DimensionsSubGrade_IsNearPerfect()
    {
        // Arrange - dimensions are assumed near-perfect (195 points)
        var scores = ConditionScores.Perfect;

        // Act
        var result = _engine.EstimateGrade(scores);

        // Assert
        var dimensionsGrade = result.SubGrades["Dimensions"];
        Assert.True(dimensionsGrade >= 9.5);  // 195 points -> 9.75, rounds to 10
    }

    [Theory]
    [InlineData(10, "Gem Mint")]    // (200+200+200+200+195)/100=9.95->10="Gem Mint"
    [InlineData(9.5, "Mint+")]      // (200+190+190+190+195)/100=9.65->9.5="Mint+"
    [InlineData(9.0, "Mint+")]      // (200+180+180+180+195)/100=9.35->9.5="Mint+"
    [InlineData(8.5, "Mint")]       // (200+170+170+170+195)/100=9.05->9.0="Mint"
    [InlineData(8.0, "Mint")]       // (200+160+160+160+195)/100=8.75->9.0="Mint"
    [InlineData(7.0, "NM-MT")]      // (200+140+140+140+195)/100=8.15->8.0="NM-MT"
    public void EstimateGrade_ReturnsCorrectLabelForGrade(double grade, string expectedLabel)
    {
        // Arrange
        // TAG uses 1000-point system: 5 categories x 200 points each
        // Dimensions assumed 195 points (9.75)
        var centering = new CenteringMeasurement
        {
            LeftRightFront = 50,
            LeftRightBack = 50,
            TopBottomFront = 50,
            TopBottomBack = 50
        };

        var scores = new ConditionScores
        {
            Centering = centering,  // 10 -> 200 points
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
    public void EstimateGrade_PointsConversion_WorksCorrectly()
    {
        // Arrange - score of 5/10 should give 100 points
        var centering = new CenteringMeasurement
        {
            LeftRightFront = 50,
            LeftRightBack = 50,
            TopBottomFront = 50,
            TopBottomBack = 50
        };

        var scores = new ConditionScores
        {
            Centering = centering,  // 10 -> 200 points
            Corners = 5,             // 5 -> 100 points
            Edges = 5,               // 5 -> 100 points
            Surface = 5              // 5 -> 100 points
        };
        // Total: 200 + 100 + 100 + 100 + 195 = 695 points
        // 695 / 100 = 6.95 -> rounds to 7.0

        // Act
        var result = _engine.EstimateGrade(scores);

        // Assert
        Assert.Equal(7.0, result.Grade);
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
    [InlineData(51, 10.0)]   // Perfect front centering
    [InlineData(53, 9.5)]    // Just above perfect
    [InlineData(55, 9.0)]    // 55/45 front
    [InlineData(58, 8.5)]    // 58/42 front
    [InlineData(60, 8.0)]    // 60/40 front
    [InlineData(63, 7.5)]    // 63/37 front
    [InlineData(65, 7.0)]    // 65/35 front
    [InlineData(70, 6.0)]    // 70/30 front
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
        Assert.Equal(0.55, result.Confidence);
    }

    [Fact]
    public void EstimateGrade_LowGrades_ShowsFormattedLabel()
    {
        // Arrange - TAG: >= 7.0 => "Near Mint", below that => "TAG X.X"
        var centering = new CenteringMeasurement
        {
            LeftRightFront = 50,
            LeftRightBack = 50,
            TopBottomFront = 50,
            TopBottomBack = 50
        };

        var scores = new ConditionScores
        {
            Centering = centering,  // 10 -> 200 points
            Corners = 4.5,          // 4.5 -> 90 points
            Edges = 4.5,            // 4.5 -> 90 points
            Surface = 4.5           // 4.5 -> 90 points
        };
        // Total: 200 + 90 + 90 + 90 + 195 = 665 / 100 = 6.65 -> 6.5 < 7.0

        // Act
        var result = _engine.EstimateGrade(scores);

        // Assert - grade 6.5 < 7.0 returns formatted "TAG 6.5"
        Assert.StartsWith("TAG", result.Label);
    }

    [Fact]
    public void EstimateGrade_MinimumGrade_IsCappedAt1()
    {
        // Arrange - very low scores
        var centering = new CenteringMeasurement
        {
            LeftRightFront = 90,  // Very poor centering
            LeftRightBack = 90,
            TopBottomFront = 90,
            TopBottomBack = 90
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
        Assert.True(result.Grade >= 1);
    }

    [Fact]
    public void EstimateGrade_MaximumGrade_IsCappedAt10()
    {
        // Arrange - perfect scores
        var scores = ConditionScores.Perfect;

        // Act
        var result = _engine.EstimateGrade(scores);

        // Assert
        Assert.True(result.Grade <= 10);
    }

    [Fact]
    public void EstimateGrade_SubGradesDisplay_ConvertedFromPoints()
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
            Centering = centering,  // 10 -> 200 points -> display as 10
            Corners = 8,             // 8 -> 160 points -> display as 8
            Edges = 9,               // 9 -> 180 points -> display as 9
            Surface = 7              // 7 -> 140 points -> display as 7
        };

        // Act
        var result = _engine.EstimateGrade(scores);

        // Assert
        Assert.Equal(10, result.SubGrades["Centering"]);
        Assert.Equal(8, result.SubGrades["Corners"]);
        Assert.Equal(9, result.SubGrades["Edges"]);
        Assert.Equal(7, result.SubGrades["Surface"]);
    }

    [Fact]
    public void EstimateGrade_FifthCategory_AddsDimensions()
    {
        // Arrange - TAG is unique in having Dimensions as a 5th category
        var scores = ConditionScores.Perfect;

        // Act
        var result = _engine.EstimateGrade(scores);

        // Assert
        Assert.Contains("Dimensions", result.SubGrades.Keys);
        // Dimensions assumed near-perfect (195 points = 9.75 -> 10)
        var dimensionsScore = result.SubGrades["Dimensions"];
        Assert.True(dimensionsScore > 0);
    }

    [Fact]
    public void EstimateGrade_Overall_IsPointsOver100()
    {
        // Arrange - known scores to verify calculation
        var centering = new CenteringMeasurement
        {
            LeftRightFront = 50,
            LeftRightBack = 50,
            TopBottomFront = 50,
            TopBottomBack = 50
        };

        var scores = new ConditionScores
        {
            Centering = centering,  // 10 -> 200 points
            Corners = 10,            // 10 -> 200 points
            Edges = 10,              // 10 -> 200 points
            Surface = 10             // 10 -> 200 points
        };
        // Total: 200 + 200 + 200 + 200 + 195 = 995 points
        // 995 / 100 = 9.95 -> rounds to 10

        // Act
        var result = _engine.EstimateGrade(scores);

        // Assert
        Assert.Equal(10, result.Grade);
    }
}
