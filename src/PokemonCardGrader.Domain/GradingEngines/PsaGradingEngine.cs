using PokemonCardGrader.Domain.Enums;
using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Domain.GradingEngines;

/// <summary>
/// PSA grading: whole numbers 1-10, no sub-grades on label.
/// Centering thresholds: 10 = 55/45, 9 = 60/40, 8 = 65/35, 7 = 70/30, 6 = 75/25.
/// Overall grade is the minimum of all four category grades.
/// </summary>
public sealed class PsaGradingEngine : IGradingRuleEngine
{
    public GradingCompany Company => GradingCompany.PSA;

    public GradeResult EstimateGrade(ConditionScores scores)
    {
        var centeringGrade = CalculateCenteringGrade(scores.Centering);
        var cornersGrade = MapToWholeGrade(scores.Corners);
        var edgesGrade = MapToWholeGrade(scores.Edges);
        var surfaceGrade = MapToWholeGrade(scores.Surface);

        var overallGrade = Math.Min(Math.Min(centeringGrade, cornersGrade),
                                     Math.Min(edgesGrade, surfaceGrade));

        var subGrades = new Dictionary<string, double>
        {
            ["Centering"] = centeringGrade,
            ["Corners"] = cornersGrade,
            ["Edges"] = edgesGrade,
            ["Surface"] = surfaceGrade
        };

        var label = overallGrade switch
        {
            10 => "Gem Mint",
            9 => "Mint",
            8 => "NM-MT",
            7 => "Near Mint",
            6 => "EX-MT",
            5 => "Excellent",
            4 => "VG-EX",
            3 => "VG",
            2 => "Good",
            _ => "Poor"
        };

        return new GradeResult
        {
            Company = GradingCompany.PSA,
            Grade = overallGrade,
            SubGrades = subGrades,
            Confidence = 0.7,
            Label = label,
            IsRuleBased = true
        };
    }

    private static int CalculateCenteringGrade(CenteringMeasurement centering)
    {
        var frontRatio = centering.FrontRatio;
        var backRatio = centering.BackRatio;

        // PSA centering is based on front centering primarily
        var frontDeviation = frontRatio.Larger;
        var backDeviation = backRatio.Larger;

        return frontDeviation switch
        {
            <= 55 when backDeviation <= 75 => 10,
            <= 60 when backDeviation <= 75 => 9,
            <= 65 when backDeviation <= 80 => 8,
            <= 70 when backDeviation <= 85 => 7,
            <= 75 when backDeviation <= 90 => 6,
            <= 80 => 5,
            <= 85 => 4,
            <= 90 => 3,
            _ => 2
        };
    }

    private static int MapToWholeGrade(double score)
    {
        return score switch
        {
            >= 9.5 => 10,
            >= 8.5 => 9,
            >= 7.5 => 8,
            >= 6.5 => 7,
            >= 5.5 => 6,
            >= 4.5 => 5,
            >= 3.5 => 4,
            >= 2.5 => 3,
            >= 1.5 => 2,
            _ => 1
        };
    }
}
