namespace PokemonCardGrader.Domain.ValueObjects;

public sealed record CenteringMeasurement
{
    public required double LeftRightFront { get; init; }
    public required double LeftRightBack { get; init; }
    public required double TopBottomFront { get; init; }
    public required double TopBottomBack { get; init; }

    /// <summary>
    /// Max deviation from 50/50 across all four measurements.
    /// PSA uses this to determine centering grade.
    /// </summary>
    public double MaxDeviation => new[]
    {
        Math.Abs(LeftRightFront - 50),
        Math.Abs(LeftRightBack - 50),
        Math.Abs(TopBottomFront - 50),
        Math.Abs(TopBottomBack - 50)
    }.Max();

    /// <summary>
    /// Front centering expressed as larger/smaller ratio (e.g., 55/45).
    /// </summary>
    public (double Larger, double Smaller) FrontRatio
    {
        get
        {
            var maxLr = Math.Max(LeftRightFront, 100 - LeftRightFront);
            var maxTb = Math.Max(TopBottomFront, 100 - TopBottomFront);
            var larger = Math.Max(maxLr, maxTb);
            return (larger, 100 - larger);
        }
    }

    /// <summary>
    /// Back centering expressed as larger/smaller ratio.
    /// </summary>
    public (double Larger, double Smaller) BackRatio
    {
        get
        {
            var maxLr = Math.Max(LeftRightBack, 100 - LeftRightBack);
            var maxTb = Math.Max(TopBottomBack, 100 - TopBottomBack);
            var larger = Math.Max(maxLr, maxTb);
            return (larger, 100 - larger);
        }
    }

    public static CenteringMeasurement Perfect => new()
    {
        LeftRightFront = 50,
        LeftRightBack = 50,
        TopBottomFront = 50,
        TopBottomBack = 50
    };
}
