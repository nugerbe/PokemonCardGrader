using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Domain.Tests.ValueObjects;

public class ConditionScoresTests
{
    [Fact]
    public void Perfect_HasAllScoresAt10()
    {
        // Act
        var perfect = ConditionScores.Perfect;

        // Assert
        Assert.Equal(10, perfect.Corners);
        Assert.Equal(10, perfect.Edges);
        Assert.Equal(10, perfect.Surface);
    }

    [Fact]
    public void Perfect_HasPerfectCentering()
    {
        // Act
        var perfect = ConditionScores.Perfect;

        // Assert
        Assert.NotNull(perfect.Centering);
        Assert.Equal(CenteringMeasurement.Perfect, perfect.Centering);
    }

    [Fact]
    public void ConditionScores_IsRecord()
    {
        // Arrange
        var centering = CenteringMeasurement.Perfect;

        var scores1 = new ConditionScores
        {
            Centering = centering,
            Corners = 9.5,
            Edges = 9.0,
            Surface = 8.5
        };

        var scores2 = new ConditionScores
        {
            Centering = centering,
            Corners = 9.5,
            Edges = 9.0,
            Surface = 8.5
        };

        // Act & Assert - records provide value equality
        Assert.Equal(scores1, scores2);
    }

    [Fact]
    public void ConditionScores_RequiresCentering()
    {
        // Act
        var scores = new ConditionScores
        {
            Centering = CenteringMeasurement.Perfect,
            Corners = 8.0,
            Edges = 7.5,
            Surface = 9.0
        };

        // Assert
        Assert.NotNull(scores.Centering);
    }

    [Fact]
    public void ConditionScores_RequiresCorners()
    {
        // Act
        var scores = new ConditionScores
        {
            Centering = CenteringMeasurement.Perfect,
            Corners = 8.0,
            Edges = 7.5,
            Surface = 9.0
        };

        // Assert
        Assert.Equal(8.0, scores.Corners);
    }

    [Fact]
    public void ConditionScores_RequiresEdges()
    {
        // Act
        var scores = new ConditionScores
        {
            Centering = CenteringMeasurement.Perfect,
            Corners = 8.0,
            Edges = 7.5,
            Surface = 9.0
        };

        // Assert
        Assert.Equal(7.5, scores.Edges);
    }

    [Fact]
    public void ConditionScores_RequiresSurface()
    {
        // Act
        var scores = new ConditionScores
        {
            Centering = CenteringMeasurement.Perfect,
            Corners = 8.0,
            Edges = 7.5,
            Surface = 9.0
        };

        // Assert
        Assert.Equal(9.0, scores.Surface);
    }

    [Fact]
    public void ConditionScores_AcceptsHalfIncrements()
    {
        // Arrange
        var centering = CenteringMeasurement.Perfect;

        // Act
        var scores = new ConditionScores
        {
            Centering = centering,
            Corners = 9.5,
            Edges = 8.5,
            Surface = 7.5
        };

        // Assert
        Assert.Equal(9.5, scores.Corners);
        Assert.Equal(8.5, scores.Edges);
        Assert.Equal(7.5, scores.Surface);
    }

    [Fact]
    public void ConditionScores_AcceptsWholeNumbers()
    {
        // Arrange
        var centering = CenteringMeasurement.Perfect;

        // Act
        var scores = new ConditionScores
        {
            Centering = centering,
            Corners = 10,
            Edges = 8,
            Surface = 6
        };

        // Assert
        Assert.Equal(10, scores.Corners);
        Assert.Equal(8, scores.Edges);
        Assert.Equal(6, scores.Surface);
    }

    [Fact]
    public void ConditionScores_AcceptsScoresFrom1To10()
    {
        // Arrange
        var centering = CenteringMeasurement.Perfect;

        // Act
        var minScores = new ConditionScores
        {
            Centering = centering,
            Corners = 1,
            Edges = 1,
            Surface = 1
        };

        var maxScores = new ConditionScores
        {
            Centering = centering,
            Corners = 10,
            Edges = 10,
            Surface = 10
        };

        // Assert
        Assert.Equal(1, minScores.Corners);
        Assert.Equal(10, maxScores.Corners);
    }

    [Fact]
    public void ConditionScores_WithDifferentCentering_AreNotEqual()
    {
        // Arrange
        var centering1 = CenteringMeasurement.Perfect;
        var centering2 = new CenteringMeasurement
        {
            LeftRightFront = 60,
            LeftRightBack = 50,
            TopBottomFront = 50,
            TopBottomBack = 50
        };

        var scores1 = new ConditionScores
        {
            Centering = centering1,
            Corners = 9.5,
            Edges = 9.0,
            Surface = 8.5
        };

        var scores2 = new ConditionScores
        {
            Centering = centering2,
            Corners = 9.5,
            Edges = 9.0,
            Surface = 8.5
        };

        // Act & Assert
        Assert.NotEqual(scores1, scores2);
    }

    [Fact]
    public void ConditionScores_WithDifferentCorners_AreNotEqual()
    {
        // Arrange
        var centering = CenteringMeasurement.Perfect;

        var scores1 = new ConditionScores
        {
            Centering = centering,
            Corners = 9.5,
            Edges = 9.0,
            Surface = 8.5
        };

        var scores2 = new ConditionScores
        {
            Centering = centering,
            Corners = 9.0,
            Edges = 9.0,
            Surface = 8.5
        };

        // Act & Assert
        Assert.NotEqual(scores1, scores2);
    }

    [Fact]
    public void ConditionScores_WithAllDifferentScores_AreDistinct()
    {
        // Arrange
        var centering = CenteringMeasurement.Perfect;

        // Act
        var scores = new ConditionScores
        {
            Centering = centering,
            Corners = 10,
            Edges = 9,
            Surface = 8
        };

        // Assert - verify all scores can be different
        Assert.NotEqual(scores.Corners, scores.Edges);
        Assert.NotEqual(scores.Edges, scores.Surface);
        Assert.NotEqual(scores.Corners, scores.Surface);
    }

    [Fact]
    public void Perfect_IsSingletonLike()
    {
        // Act
        var perfect1 = ConditionScores.Perfect;
        var perfect2 = ConditionScores.Perfect;

        // Assert - should return same instance or equal instances
        Assert.Equal(perfect1, perfect2);
    }

    [Fact]
    public void ConditionScores_CanBeUsedWithDecimalPrecision()
    {
        // Arrange
        var centering = CenteringMeasurement.Perfect;

        // Act
        var scores = new ConditionScores
        {
            Centering = centering,
            Corners = 9.75,
            Edges = 8.25,
            Surface = 7.125
        };

        // Assert - verify decimal precision is preserved
        Assert.Equal(9.75, scores.Corners);
        Assert.Equal(8.25, scores.Edges);
        Assert.Equal(7.125, scores.Surface);
    }
}
