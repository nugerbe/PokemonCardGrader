using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Infrastructure.ImageAnalysis;

/// <summary>
/// Builds structured, human-readable <see cref="GradingReport"/> from analysis
/// results and grade output. Provides explainability for every aspect of the grade.
/// </summary>
public static class GradingReportBuilder
{
    /// <summary>
    /// Builds a complete grading report from analysis results and an optional grade result.
    /// </summary>
    public static GradingReport Build(ImageAnalysisResult analysis, GradeResult? grade)
    {
        var centering = BuildCenteringSummary(analysis.DetectedCentering);
        var surface = BuildSurfaceSummary(analysis.SurfaceScore, analysis.MlSurfaceScore, analysis.SurfaceModelUsed);
        var corners = BuildCornersSummary(analysis.CornersScore);
        var edges = BuildEdgesSummary(analysis.EdgesScore);
        var defectNotes = BuildDefectNotes(analysis.DetectedDefects, analysis.MlDetectedDefects);
        var confidenceNote = BuildConfidenceNote(analysis.ConfidenceDetail);
        var gradeJustifications = BuildGradeJustifications(analysis, grade);
        var mlContribution = BuildMlContribution(analysis);
        var overall = BuildOverallSummary(analysis, grade);

        return new GradingReport
        {
            OverallSummary = overall,
            CenteringSummary = centering,
            SurfaceSummary = surface,
            CornersSummary = corners,
            EdgesSummary = edges,
            DefectNotes = defectNotes,
            ConfidenceNote = confidenceNote,
            GradeJustifications = gradeJustifications,
            MlContribution = mlContribution
        };
    }

    private static string BuildOverallSummary(ImageAnalysisResult analysis, GradeResult? grade)
    {
        if (grade is null)
            return "Analysis complete but no grade could be computed.";

        var gradeLabel = grade.GradeLabel;
        var companyName = grade.Company.ToString().ToUpperInvariant();
        var confidenceStr = analysis.OverallConfidence.HasValue
            ? $" ({analysis.OverallConfidence.Value:P0} confidence)"
            : "";

        var weakestArea = DetermineWeakestArea(analysis);
        var weakestNote = weakestArea is not null ? $" The weakest area is {weakestArea}." : "";

        return $"This card grades {companyName} {grade.Grade:F1} ({gradeLabel}){confidenceStr}.{weakestNote}";
    }

    private static string? DetermineWeakestArea(ImageAnalysisResult analysis)
    {
        var scores = new List<(string Name, double Score)>();
        if (analysis.CornersScore.HasValue) scores.Add(("corners", analysis.CornersScore.Value));
        if (analysis.EdgesScore.HasValue) scores.Add(("edges", analysis.EdgesScore.Value));
        if (analysis.SurfaceScore.HasValue) scores.Add(("surface", analysis.SurfaceScore.Value));

        if (analysis.DetectedCentering is not null)
        {
            // Convert centering deviation to a 1-10 score for comparison
            var deviation = analysis.DetectedCentering.MaxDeviation;
            var centeringScore = Math.Clamp(10.0 - deviation * 0.2, 1.0, 10.0);
            scores.Add(("centering", centeringScore));
        }

        if (scores.Count == 0) return null;

        var weakest = scores.MinBy(s => s.Score);
        return weakest.Score < 9.0 ? weakest.Name : null;
    }

    private static string BuildCenteringSummary(CenteringMeasurement? centering)
    {
        if (centering is null)
            return "Centering could not be measured.";

        var lr = centering.LeftRightFront;
        var tb = centering.TopBottomFront;
        var lrDesc = DescribeCenteringRatio(lr);
        var tbDesc = DescribeCenteringRatio(tb);

        var lrFormatted = FormatCenteringSplit(lr);
        var tbFormatted = FormatCenteringSplit(tb);

        return $"Centering: L/R {lrFormatted} ({lrDesc}), T/B {tbFormatted} ({tbDesc}).";
    }

    private static string FormatCenteringSplit(double bigSide)
    {
        var smallSide = 100.0 - bigSide;
        var left = Math.Max(bigSide, smallSide);
        var right = Math.Min(bigSide, smallSide);
        return $"{left:F0}/{right:F0}";
    }

    private static string DescribeCenteringRatio(double ratio)
    {
        var deviation = Math.Abs(ratio - 50.0);
        return deviation switch
        {
            < 2 => "excellent",
            < 5 => "very good",
            < 10 => "acceptable",
            < 15 => "off-center",
            < 25 => "significantly off-center",
            _ => "severely off-center"
        };
    }

    private static string BuildSurfaceSummary(double? cvScore, double? mlScore, bool mlUsed)
    {
        if (!cvScore.HasValue)
            return "Surface condition could not be assessed.";

        var desc = DescribeScore(cvScore.Value);
        var result = $"Surface: {cvScore.Value:F1}/10 ({desc}).";

        if (mlUsed && mlScore.HasValue)
        {
            var diff = Math.Abs(cvScore.Value - mlScore.Value);
            if (diff > 1.5)
                result += $" ML model suggests {mlScore.Value:F1}/10, which differs significantly.";
            else if (diff > 0.5)
                result += $" ML model concurs at {mlScore.Value:F1}/10.";
        }

        return result;
    }

    private static string BuildCornersSummary(double? score)
    {
        if (!score.HasValue)
            return "Corner condition could not be assessed.";

        var desc = DescribeScore(score.Value);
        var detail = score.Value switch
        {
            >= 9.5 => "No visible corner wear detected.",
            >= 8.0 => "Minor corner softening or slight whitening detected.",
            >= 6.0 => "Moderate corner wear with visible whitening or rounding.",
            >= 4.0 => "Significant corner damage or heavy whitening present.",
            _ => "Severe corner damage with substantial material loss."
        };

        return $"Corners: {score.Value:F1}/10 ({desc}). {detail}";
    }

    private static string BuildEdgesSummary(double? score)
    {
        if (!score.HasValue)
            return "Edge condition could not be assessed.";

        var desc = DescribeScore(score.Value);
        var detail = score.Value switch
        {
            >= 9.5 => "No visible edge wear detected.",
            >= 8.0 => "Minor edge wear or slight whitening on one or more edges.",
            >= 6.0 => "Moderate edge wear with visible whitening or chipping.",
            >= 4.0 => "Significant edge damage with heavy whitening or peeling.",
            _ => "Severe edge damage affecting structural integrity."
        };

        return $"Edges: {score.Value:F1}/10 ({desc}). {detail}";
    }

    private static List<string> BuildDefectNotes(
        List<DetectedDefect> cvDefects, List<DetectedDefect> mlDefects)
    {
        var notes = new List<string>();
        var allDefects = cvDefects
            .Select(d => (d, Source: "CV"))
            .Concat(mlDefects.Select(d => (d, Source: "ML")))
            .OrderByDescending(x => x.d.Severity)
            .ToList();

        if (allDefects.Count == 0)
        {
            notes.Add("No defects detected.");
            return notes;
        }

        foreach (var (defect, source) in allDefects.Take(10))
        {
            var severityDesc = defect.Severity switch
            {
                > 0.7 => "severe",
                > 0.4 => "moderate",
                > 0.2 => "minor",
                _ => "trace"
            };

            var locationDesc = DescribeLocation(defect.X, defect.Y);
            notes.Add(
                $"{severityDesc} {defect.Type} at {locationDesc} " +
                $"(confidence: {defect.Confidence:P0}, detected by {source})");
        }

        return notes;
    }

    private static string DescribeLocation(double x, double y)
    {
        var vertical = y switch
        {
            < 0.33 => "top",
            > 0.66 => "bottom",
            _ => "center"
        };
        var horizontal = x switch
        {
            < 0.33 => "left",
            > 0.66 => "right",
            _ => "center"
        };

        return vertical == "center" && horizontal == "center"
            ? "center"
            : $"{vertical}-{horizontal}";
    }

    private static string BuildConfidenceNote(ConfidenceBreakdown? confidence)
    {
        if (confidence is null)
            return "Confidence assessment was not performed.";

        return confidence.Summary;
    }

    private static List<string> BuildGradeJustifications(
        ImageAnalysisResult analysis, GradeResult? grade)
    {
        var justifications = new List<string>();

        if (grade is null)
        {
            justifications.Add("No grade could be computed from the available data.");
            return justifications;
        }

        // Identify limiting subgrade
        if (grade.SubGrades.Count > 0)
        {
            var minSub = grade.SubGrades.MinBy(kv => kv.Value);
            var maxSub = grade.SubGrades.MaxBy(kv => kv.Value);

            justifications.Add(
                $"The grade is primarily limited by {minSub.Key} " +
                $"(sub-grade: {minSub.Value:F1}/10).");

            if (maxSub.Key != minSub.Key)
            {
                justifications.Add(
                    $"Strongest area: {maxSub.Key} " +
                    $"(sub-grade: {maxSub.Value:F1}/10).");
            }
        }

        // Centering impact
        if (analysis.DetectedCentering is not null)
        {
            var maxDev = analysis.DetectedCentering.MaxDeviation;
            if (maxDev > 10)
            {
                justifications.Add(
                    $"Off-center printing ({maxDev:F0}% deviation) reduces the centering sub-grade.");
            }
        }

        // Defect impact
        var highSeverityDefects = analysis.DetectedDefects
            .Concat(analysis.MlDetectedDefects)
            .Count(d => d.Severity > 0.5);

        if (highSeverityDefects > 0)
        {
            justifications.Add(
                $"{highSeverityDefects} significant defect(s) detected, impacting surface and edge scores.");
        }

        // Confidence caveat
        if (analysis.OverallConfidence.HasValue && analysis.OverallConfidence.Value < 0.5)
        {
            justifications.Add(
                "Low analysis confidence — grade should be verified with additional images.");
        }

        // Rule-based vs ML
        if (grade.IsRuleBased)
            justifications.Add("Grade computed using rule-based engine (insufficient ML training data).");
        else
            justifications.Add("Grade computed using ML prediction calibrated against rule-based baseline.");

        return justifications;
    }

    private static string BuildMlContribution(ImageAnalysisResult analysis)
    {
        var parts = new List<string>();

        if (analysis.DefectModelUsed)
        {
            parts.Add($"defect detection ({analysis.MlDetectedDefects.Count} findings)");
        }

        if (analysis.SurfaceModelUsed && analysis.MlSurfaceScore.HasValue)
        {
            parts.Add($"surface grading ({analysis.MlSurfaceScore.Value:F1}/10)");
        }

        if (parts.Count == 0)
            return "No ML models were available during analysis. Results are CV-only.";

        return $"ML models contributed: {string.Join(", ", parts)}.";
    }

    private static string DescribeScore(double score) => score switch
    {
        >= 9.5 => "gem mint",
        >= 9.0 => "mint",
        >= 8.0 => "near mint",
        >= 7.0 => "excellent",
        >= 6.0 => "very good",
        >= 5.0 => "good",
        >= 4.0 => "fair",
        _ => "poor"
    };
}
