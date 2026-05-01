using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Domain.Tests.ValueObjects;

public class CenteringMeasurementTests
{
    [Fact]
    public void Perfect_HasAllMeasurementsAt50()
    {
        // Act
        var perfect = CenteringMeasurement.Perfect;

        // Assert
        Assert.Equal(50, perfect.LeftRightFront);
        Assert.Equal(50, perfect.LeftRightBack);
        Assert.Equal(50, perfect.TopBottomFront);
        Assert.Equal(50, perfect.TopBottomBack);
    }

    [Fact]
    public void MaxDeviation_WithPerfectCentering_ReturnsZero()
    {
        // Arrange
        var perfect = CenteringMeasurement.Perfect;

        // Act
        var maxDeviation = perfect.MaxDeviation;

        // Assert
        Assert.Equal(0, maxDeviation);
    }

    [Fact]
    public void MaxDeviation_WithSingleOffMeasurement_ReturnsCorrectValue()
    {
        // Arrange - only left/right front is off
        var centering = new CenteringMeasurement
        {
            LeftRightFront = 60,  // 10% deviation from 50
            LeftRightBack = 50,
            TopBottomFront = 50,
            TopBottomBack = 50
        };

        // Act
        var maxDeviation = centering.MaxDeviation;

        // Assert
        Assert.Equal(10, maxDeviation);
    }

    [Fact]
    public void MaxDeviation_FindsMaximumAcrossAllFour()
    {
        // Arrange - different deviations
        var centering = new CenteringMeasurement
        {
            LeftRightFront = 55,   // 5% deviation
            LeftRightBack = 60,    // 10% deviation
            TopBottomFront = 45,   // 5% deviation (abs(45-50))
            TopBottomBack = 70     // 20% deviation - this is max
        };

        // Act
        var maxDeviation = centering.MaxDeviation;

        // Assert
        Assert.Equal(20, maxDeviation);
    }

    [Fact]
    public void MaxDeviation_HandlesNegativeDeviations()
    {
        // Arrange - measurement below 50
        var centering = new CenteringMeasurement
        {
            LeftRightFront = 35,  // -15% from 50, abs = 15
            LeftRightBack = 50,
            TopBottomFront = 50,
            TopBottomBack = 50
        };

        // Act
        var maxDeviation = centering.MaxDeviation;

        // Assert
        Assert.Equal(15, maxDeviation);
    }

    [Fact]
    public void FrontRatio_WithPerfectCentering_Returns50_50()
    {
        // Arrange
        var perfect = CenteringMeasurement.Perfect;

        // Act
        var (larger, smaller) = perfect.FrontRatio;

        // Assert
        Assert.Equal(50, larger);
        Assert.Equal(50, smaller);
    }

    [Fact]
    public void FrontRatio_WithLeftRightOff_ReturnsCorrectRatio()
    {
        // Arrange - left/right is 60/40
        var centering = new CenteringMeasurement
        {
            LeftRightFront = 60,
            LeftRightBack = 50,
            TopBottomFront = 50,
            TopBottomBack = 50
        };

        // Act
        var (larger, smaller) = centering.FrontRatio;

        // Assert
        Assert.Equal(60, larger);
        Assert.Equal(40, smaller);
    }

    [Fact]
    public void FrontRatio_WithTopBottomOff_ReturnsCorrectRatio()
    {
        // Arrange - top/bottom is 65/35
        var centering = new CenteringMeasurement
        {
            LeftRightFront = 50,
            LeftRightBack = 50,
            TopBottomFront = 65,
            TopBottomBack = 50
        };

        // Act
        var (larger, smaller) = centering.FrontRatio;

        // Assert
        Assert.Equal(65, larger);
        Assert.Equal(35, smaller);
    }

    [Fact]
    public void FrontRatio_TakesWorstOfLeftRightOrTopBottom()
    {
        // Arrange - both left/right and top/bottom are off, but top/bottom is worse
        var centering = new CenteringMeasurement
        {
            LeftRightFront = 55,  // 55/45 ratio
            LeftRightBack = 50,
            TopBottomFront = 70,  // 70/30 ratio - worse
            TopBottomBack = 50
        };

        // Act
        var (larger, smaller) = centering.FrontRatio;

        // Assert
        Assert.Equal(70, larger);
        Assert.Equal(30, smaller);
    }

    [Fact]
    public void FrontRatio_HandlesValuesBelow50()
    {
        // Arrange - measurement is 40, so ratio is 60/40
        var centering = new CenteringMeasurement
        {
            LeftRightFront = 40,  // Smaller side is 40, larger is 60
            LeftRightBack = 50,
            TopBottomFront = 50,
            TopBottomBack = 50
        };

        // Act
        var (larger, smaller) = centering.FrontRatio;

        // Assert
        Assert.Equal(60, larger);
        Assert.Equal(40, smaller);
    }

    [Fact]
    public void BackRatio_WithPerfectCentering_Returns50_50()
    {
        // Arrange
        var perfect = CenteringMeasurement.Perfect;

        // Act
        var (larger, smaller) = perfect.BackRatio;

        // Assert
        Assert.Equal(50, larger);
        Assert.Equal(50, smaller);
    }

    [Fact]
    public void BackRatio_WithLeftRightOff_ReturnsCorrectRatio()
    {
        // Arrange - back left/right is 70/30
        var centering = new CenteringMeasurement
        {
            LeftRightFront = 50,
            LeftRightBack = 70,
            TopBottomFront = 50,
            TopBottomBack = 50
        };

        // Act
        var (larger, smaller) = centering.BackRatio;

        // Assert
        Assert.Equal(70, larger);
        Assert.Equal(30, smaller);
    }

    [Fact]
    public void BackRatio_WithTopBottomOff_ReturnsCorrectRatio()
    {
        // Arrange - back top/bottom is 75/25
        var centering = new CenteringMeasurement
        {
            LeftRightFront = 50,
            LeftRightBack = 50,
            TopBottomFront = 50,
            TopBottomBack = 75
        };

        // Act
        var (larger, smaller) = centering.BackRatio;

        // Assert
        Assert.Equal(75, larger);
        Assert.Equal(25, smaller);
    }

    [Fact]
    public void BackRatio_TakesWorstOfLeftRightOrTopBottom()
    {
        // Arrange - both back measurements are off
        var centering = new CenteringMeasurement
        {
            LeftRightFront = 50,
            LeftRightBack = 60,  // 60/40 ratio
            TopBottomFront = 50,
            TopBottomBack = 80   // 80/20 ratio - worse
        };

        // Act
        var (larger, smaller) = centering.BackRatio;

        // Assert
        Assert.Equal(80, larger);
        Assert.Equal(20, smaller);
    }

    [Fact]
    public void BackRatio_HandlesValuesBelow50()
    {
        // Arrange - back measurement is 35, so ratio is 65/35
        var centering = new CenteringMeasurement
        {
            LeftRightFront = 50,
            LeftRightBack = 35,
            TopBottomFront = 50,
            TopBottomBack = 50
        };

        // Act
        var (larger, smaller) = centering.BackRatio;

        // Assert
        Assert.Equal(65, larger);
        Assert.Equal(35, smaller);
    }

    [Fact]
    public void FrontAndBackRatios_AreIndependent()
    {
        // Arrange - front and back have different centering
        var centering = new CenteringMeasurement
        {
            LeftRightFront = 55,  // Front: 55/45
            LeftRightBack = 70,   // Back: 70/30
            TopBottomFront = 50,
            TopBottomBack = 50
        };

        // Act
        var frontRatio = centering.FrontRatio;
        var backRatio = centering.BackRatio;

        // Assert
        Assert.Equal(55, frontRatio.Larger);
        Assert.Equal(45, frontRatio.Smaller);
        Assert.Equal(70, backRatio.Larger);
        Assert.Equal(30, backRatio.Smaller);
    }

    [Fact]
    public void CenteringMeasurement_IsRecord()
    {
        // Arrange
        var centering1 = new CenteringMeasurement
        {
            LeftRightFront = 55,
            LeftRightBack = 60,
            TopBottomFront = 50,
            TopBottomBack = 50
        };

        var centering2 = new CenteringMeasurement
        {
            LeftRightFront = 55,
            LeftRightBack = 60,
            TopBottomFront = 50,
            TopBottomBack = 50
        };

        // Act & Assert - records provide value equality
        Assert.Equal(centering1, centering2);
    }

    [Fact]
    public void CenteringMeasurement_RequiresAllProperties()
    {
        // Act & Assert - required properties must be set
        var centering = new CenteringMeasurement
        {
            LeftRightFront = 50,
            LeftRightBack = 50,
            TopBottomFront = 50,
            TopBottomBack = 50
        };

        Assert.NotNull(centering);
    }

    [Fact]
    public void MaxDeviation_WithExtremeMiscentering_ReturnsCorrectValue()
    {
        // Arrange - extremely off-center (90/10)
        var centering = new CenteringMeasurement
        {
            LeftRightFront = 90,
            LeftRightBack = 50,
            TopBottomFront = 50,
            TopBottomBack = 50
        };

        // Act
        var maxDeviation = centering.MaxDeviation;

        // Assert
        Assert.Equal(40, maxDeviation);  // |90 - 50| = 40
    }
}
