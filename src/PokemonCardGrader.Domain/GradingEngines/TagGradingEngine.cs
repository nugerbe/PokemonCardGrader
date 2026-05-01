using PokemonCardGrader.Domain.Enums;
using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Domain.GradingEngines;

/// <summary>
/// TAG (Technical Authentication and Grading): Internal 1000-point system.
/// 5 sub-categories: Centering, Corners, Edges, Surface, and Dimensions.
/// Each sub-category scored out of 200 points internally, mapped to 1-10 for display.
/// Overall = total points / 100, rounded to nearest 0.5.
/// </summary>
public sealed class TagGradingEngine : IGradingRuleEngine
{
    public GradingCompany Company => GradingCompany.TAG;

    private const double MaxPointsPerCategory = 200.0;
    private const double TotalMaxPoints = 1000.0;

    public GradeResult EstimateGrade(ConditionScores scores)
    {
        var centeringPoints = ScoreToPoints(CalculateCenteringScore(scores.Centering));
        var cornersPoints = ScoreToPoints(scores.Corners);
        var edgesPoints = ScoreToPoints(scores.Edges);
        var surfacePoints = ScoreToPoints(scores.Surface);
        // Dimensions sub-grade: TAG checks card dimensions, assume near-perfect for estimation
        var dimensionsPoints = 195.0;

        var totalPoints = centeringPoints + cornersPoints + edgesPoints + surfacePoints + dimensionsPoints;
        var overallGrade = RoundToHalf(totalPoints / 100.0);
        overallGrade = Math.Clamp(overallGrade, 1, 10);

        var subGrades = new Dictionary<string, double>
        {
            ["Centering"] = PointsToDisplay(centeringPoints),
            ["Corners"] = PointsToDisplay(cornersPoints),
            ["Edges"] = PointsToDisplay(edgesPoints),
            ["Surface"] = PointsToDisplay(surfacePoints),
            ["Dimensions"] = PointsToDisplay(dimensionsPoints)
        };

        var label = overallGrade switch
        {
            10 => "Gem Mint",
            >= 9.5 => "Mint+",
            >= 9.0 => "Mint",
            >= 8.5 => "NM-MT+",
            >= 8.0 => "NM-MT",
            >= 7.0 => "Near Mint",
            _ => $"TAG {overallGrade:F1}"
        };

        return new GradeResult
        {
            Company = GradingCompany.TAG,
            Grade = overallGrade,
            SubGrades = subGrades,
            Confidence = 0.55,
            Label = label,
            IsRuleBased = true
        };
    }

    private static double CalculateCenteringScore(CenteringMeasurement centering)
    {
        var frontDev = centering.FrontRatio.Larger;

        return frontDev switch
        {
            <= 51 => 10.0,
            <= 53 => 9.5,
            <= 55 => 9.0,
            <= 58 => 8.5,
            <= 60 => 8.0,
            <= 63 => 7.5,
            <= 65 => 7.0,
            <= 70 => 6.0,
            _ => 5.0
        };
    }

    private static double ScoreToPoints(double score)
    {
        return (score / 10.0) * MaxPointsPerCategory;
    }

    private static double PointsToDisplay(double points)
    {
        return RoundToHalf((points / MaxPointsPerCategory) * 10.0);
    }

    private static double RoundToHalf(double value)
    {
        return Math.Round(value * 2, MidpointRounding.AwayFromZero) / 2.0;
    }
}
