using PokemonCardGrader.Domain.Enums;
using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Domain.GradingEngines;

/// <summary>
/// SGC (Sportscard Guaranty Corporation): Single overall grade, no sub-grades on label.
/// Pristine 10 vs Gem Mint 10 distinction.
/// Scale: 1-10 in 0.5 increments.
/// </summary>
public sealed class SgcGradingEngine : IGradingRuleEngine
{
    public GradingCompany Company => GradingCompany.SGC;

    public GradeResult EstimateGrade(ConditionScores scores)
    {
        var centeringScore = CalculateCenteringScore(scores.Centering);
        var cornersScore = RoundToHalf(scores.Corners);
        var edgesScore = RoundToHalf(scores.Edges);
        var surfaceScore = RoundToHalf(scores.Surface);

        // SGC uses a weighted average favoring surface and corners
        var weightedAvg = (centeringScore * 0.15) +
                          (cornersScore * 0.25) +
                          (edgesScore * 0.20) +
                          (surfaceScore * 0.40);

        var overallGrade = RoundToHalf(weightedAvg);

        var subGrades = new Dictionary<string, double>
        {
            ["Centering"] = centeringScore,
            ["Corners"] = cornersScore,
            ["Edges"] = edgesScore,
            ["Surface"] = surfaceScore
        };

        var isPristine = centeringScore == 10 && cornersScore == 10 &&
                         edgesScore == 10 && surfaceScore == 10;

        var label = overallGrade switch
        {
            10 when isPristine => "Pristine Gold",
            10 => "Gem Mint",
            >= 9.5 => "Mint+",
            >= 9.0 => "Mint",
            >= 8.5 => "NM-MT+",
            >= 8.0 => "NM-MT",
            >= 7.0 => "Near Mint",
            >= 6.0 => "EX-MT",
            _ => $"SGC {overallGrade:F1}"
        };

        return new GradeResult
        {
            Company = GradingCompany.SGC,
            Grade = overallGrade,
            SubGrades = subGrades,
            Confidence = 0.6,
            Label = label,
            IsRuleBased = true
        };
    }

    private static double CalculateCenteringScore(CenteringMeasurement centering)
    {
        var frontDev = centering.FrontRatio.Larger;

        return frontDev switch
        {
            <= 52 => 10.0,
            <= 55 => 9.5,
            <= 58 => 9.0,
            <= 60 => 8.5,
            <= 63 => 8.0,
            <= 65 => 7.5,
            <= 70 => 7.0,
            <= 75 => 6.0,
            _ => 5.0
        };
    }

    private static double RoundToHalf(double value)
    {
        return Math.Round(value * 2, MidpointRounding.AwayFromZero) / 2.0;
    }
}
