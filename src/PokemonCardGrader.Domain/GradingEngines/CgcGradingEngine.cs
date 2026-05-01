using PokemonCardGrader.Domain.Enums;
using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Domain.GradingEngines;

/// <summary>
/// CGC (Certified Guaranty Company): Sub-grades, three tiers of 10.
/// Perfect 10: all sub-grades 10.
/// Pristine 10: all sub-grades 9.5+, at least one 10.
/// Gem Mint 10: overall qualifies for 10 based on weighted average.
/// Scale: 1-10 in 0.5 increments.
/// </summary>
public sealed class CgcGradingEngine : IGradingRuleEngine
{
    public GradingCompany Company => GradingCompany.CGC;

    public GradeResult EstimateGrade(ConditionScores scores)
    {
        var centeringSub = CalculateCenteringSub(scores.Centering);
        var cornersSub = RoundToHalf(scores.Corners);
        var edgesSub = RoundToHalf(scores.Edges);
        var surfaceSub = RoundToHalf(scores.Surface);

        var average = (centeringSub + cornersSub + edgesSub + surfaceSub) / 4.0;
        var overallGrade = RoundToHalf(average);

        var subGrades = new Dictionary<string, double>
        {
            ["Centering"] = centeringSub,
            ["Corners"] = cornersSub,
            ["Edges"] = edgesSub,
            ["Surface"] = surfaceSub
        };

        var label = DetermineLabel(overallGrade, centeringSub, cornersSub, edgesSub, surfaceSub);

        return new GradeResult
        {
            Company = GradingCompany.CGC,
            Grade = overallGrade,
            SubGrades = subGrades,
            Confidence = 0.65,
            Label = label,
            IsRuleBased = true
        };
    }

    private static double CalculateCenteringSub(CenteringMeasurement centering)
    {
        var frontDev = centering.FrontRatio.Larger;
        var backDev = centering.BackRatio.Larger;

        var frontScore = frontDev switch
        {
            <= 51 => 10.0,
            <= 53 => 9.5,
            <= 55 => 9.0,
            <= 58 => 8.5,
            <= 60 => 8.0,
            <= 63 => 7.5,
            <= 65 => 7.0,
            <= 70 => 6.0,
            <= 75 => 5.0,
            _ => 4.0
        };

        var backScore = backDev switch
        {
            <= 60 => 10.0,
            <= 65 => 9.5,
            <= 70 => 9.0,
            <= 75 => 8.0,
            <= 80 => 7.0,
            _ => 5.0
        };

        return RoundToHalf(Math.Min(frontScore, backScore));
    }

    private static string DetermineLabel(double overall, double centering, double corners, double edges, double surface)
    {
        if (overall >= 10)
        {
            if (centering == 10 && corners == 10 && edges == 10 && surface == 10)
                return "Perfect";

            if (centering >= 9.5 && corners >= 9.5 && edges >= 9.5 && surface >= 9.5)
                return "Pristine";

            return "Gem Mint";
        }

        return overall switch
        {
            >= 9.5 => "Gem Mint",
            >= 9.0 => "Mint",
            >= 8.5 => "NM/Mint+",
            >= 8.0 => "NM/Mint",
            >= 7.5 => "Near Mint+",
            >= 7.0 => "Near Mint",
            _ => $"CGC {overall:F1}"
        };
    }

    private static double RoundToHalf(double value)
    {
        return Math.Round(value * 2, MidpointRounding.AwayFromZero) / 2.0;
    }
}
