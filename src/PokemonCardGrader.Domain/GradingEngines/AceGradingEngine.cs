using PokemonCardGrader.Domain.Enums;
using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Domain.GradingEngines;

/// <summary>
/// ACE Grading: Sub-grades in 0.5 increments.
/// Overall grade is capped at the lowest sub-grade + 1.
/// Scale: 1-10.
/// </summary>
public sealed class AceGradingEngine : IGradingRuleEngine
{
    public GradingCompany Company => GradingCompany.ACE;

    public GradeResult EstimateGrade(ConditionScores scores)
    {
        var centeringSub = CalculateCenteringSub(scores.Centering);
        var cornersSub = RoundToHalf(scores.Corners);
        var edgesSub = RoundToHalf(scores.Edges);
        var surfaceSub = RoundToHalf(scores.Surface);

        var average = (centeringSub + cornersSub + edgesSub + surfaceSub) / 4.0;
        var lowestSub = Math.Min(Math.Min(centeringSub, cornersSub),
                                  Math.Min(edgesSub, surfaceSub));

        // ACE caps overall at lowest sub-grade + 1
        var overallGrade = RoundToHalf(Math.Min(average, lowestSub + 1));
        overallGrade = Math.Min(overallGrade, 10);

        var subGrades = new Dictionary<string, double>
        {
            ["Centering"] = centeringSub,
            ["Corners"] = cornersSub,
            ["Edges"] = edgesSub,
            ["Surface"] = surfaceSub
        };

        var label = overallGrade switch
        {
            10 => "Gem Mint",
            >= 9.5 => "Mint+",
            >= 9.0 => "Mint",
            >= 8.5 => "NM-MT+",
            >= 8.0 => "NM-MT",
            >= 7.0 => "Near Mint",
            _ => $"ACE {overallGrade:F1}"
        };

        return new GradeResult
        {
            Company = GradingCompany.ACE,
            Grade = overallGrade,
            SubGrades = subGrades,
            Confidence = 0.6,
            Label = label,
            IsRuleBased = true
        };
    }

    private static double CalculateCenteringSub(CenteringMeasurement centering)
    {
        var frontDev = centering.FrontRatio.Larger;

        return frontDev switch
        {
            <= 52 => 10.0,
            <= 55 => 9.5,
            <= 57 => 9.0,
            <= 60 => 8.5,
            <= 63 => 8.0,
            <= 65 => 7.5,
            <= 68 => 7.0,
            <= 72 => 6.0,
            <= 78 => 5.0,
            _ => 4.0
        };
    }

    private static double RoundToHalf(double value)
    {
        return Math.Round(value * 2, MidpointRounding.AwayFromZero) / 2.0;
    }
}
