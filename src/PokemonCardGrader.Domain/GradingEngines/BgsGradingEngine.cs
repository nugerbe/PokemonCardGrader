using PokemonCardGrader.Domain.Enums;
using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Domain.GradingEngines;

/// <summary>
/// BGS (Beckett Grading Services): Sub-grades in 0.5 increments.
/// Weighted formula: Centering(10%) + Corners(25%) + Edges(25%) + Surface(40%).
/// Labels: 10 Black Label (all 10s), 10 Gold Label (9.5+ subs), 9.5 Gem Mint, etc.
/// </summary>
public sealed class BgsGradingEngine : IGradingRuleEngine
{
    public GradingCompany Company => GradingCompany.BGS;

    private const double CenteringWeight = 0.10;
    private const double CornersWeight = 0.25;
    private const double EdgesWeight = 0.25;
    private const double SurfaceWeight = 0.40;

    public GradeResult EstimateGrade(ConditionScores scores)
    {
        var centeringSub = CalculateCenteringSub(scores.Centering);
        var cornersSub = RoundToHalf(scores.Corners);
        var edgesSub = RoundToHalf(scores.Edges);
        var surfaceSub = RoundToHalf(scores.Surface);

        var weightedAvg = (centeringSub * CenteringWeight) +
                          (cornersSub * CornersWeight) +
                          (edgesSub * EdgesWeight) +
                          (surfaceSub * SurfaceWeight);

        var overallGrade = RoundToHalf(weightedAvg);

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
            Company = GradingCompany.BGS,
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
            <= 50.5 => 10.0,
            <= 52 => 9.5,
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
            <= 55 => 10.0,
            <= 60 => 9.5,
            <= 65 => 9.0,
            <= 70 => 8.5,
            <= 75 => 8.0,
            <= 80 => 7.0,
            _ => 5.0
        };

        return RoundToHalf(Math.Min(frontScore, backScore));
    }

    private static string DetermineLabel(double overall, double centering, double corners, double edges, double surface)
    {
        if (centering == 10 && corners == 10 && edges == 10 && surface == 10)
            return "Black Label";

        if (centering >= 9.5 && corners >= 9.5 && edges >= 9.5 && surface >= 9.5 && overall >= 10)
            return "Gold Label (Pristine)";

        return overall switch
        {
            >= 9.5 => "Gem Mint",
            >= 9.0 => "Mint",
            >= 8.5 => "NM-MT+",
            >= 8.0 => "NM-MT",
            >= 7.5 => "NM+",
            >= 7.0 => "Near Mint",
            _ => $"BGS {overall:F1}"
        };
    }

    private static double RoundToHalf(double value)
    {
        return Math.Round(value * 2, MidpointRounding.AwayFromZero) / 2.0;
    }
}
