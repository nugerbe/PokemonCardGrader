using Microsoft.Extensions.Logging;
using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Infrastructure.ImageAnalysis;

/// <summary>
/// Phase 24: Assembles the final structured grading output from all pipeline stages.
/// Combines analysis results, grade estimates, reports, and metadata into the
/// FinalGradingOutput record for API responses and UI display.
/// </summary>
public sealed class FinalOutputAssembler(
    RegionSegmenter regionSegmenter,
    ILogger<FinalOutputAssembler> logger)
{
    /// <summary>
    /// Assembles the complete grading output from pipeline results.
    /// </summary>
    public FinalGradingOutput Assemble(
        Guid submissionId,
        ImageAnalysisResult analysis,
        List<GradeResult> grades,
        GradingReport report,
        ImageQualityAssessment? qualityAssessment,
        FailureDetectionResult? failureDetection)
    {
        // Build condition scores
        var scores = new ConditionScores
        {
            Centering = analysis.DetectedCentering ?? CenteringMeasurement.Perfect,
            Corners = analysis.CornersScore ?? 5.0,
            Edges = analysis.EdgesScore ?? 5.0,
            Surface = analysis.SurfaceScore ?? 5.0
        };

        // Classify defects into regions
        var defectReports = ClassifyDefects(analysis, scores);

        // Determine overall confidence
        var confidence = analysis.OverallConfidence ?? 0.5;

        // Determine analysis method
        var method = analysis.AnalysisMethod;
        if (analysis.DefectModelUsed || analysis.SurfaceModelUsed)
        {
            method = "OpenCV-v4+ML";
        }

        var output = new FinalGradingOutput
        {
            SubmissionId = submissionId,
            CompletedAt = DateTimeOffset.UtcNow,
            Grades = grades,
            Confidence = confidence,
            ConfidenceDetail = analysis.ConfidenceDetail,
            Centering = analysis.DetectedCentering,
            Scores = scores,
            Defects = defectReports,
            Report = report,
            QualityAssessment = qualityAssessment,
            FailureDetection = failureDetection,
            AnalysisMethod = method,
            Overlay = analysis.Overlay
        };

        logger.LogInformation(
            "FinalOutput assembled: submission={Id} grades={GradeCount} " +
            "defects={DefectCount} confidence={Confidence:F2}",
            submissionId, grades.Count, defectReports.Count, confidence);

        return output;
    }

    private List<DefectReport> ClassifyDefects(ImageAnalysisResult analysis, ConditionScores scores)
    {
        var allDefects = new List<DetectedDefect>(analysis.DetectedDefects);
        if (analysis.MlDetectedDefects.Count > 0)
        {
            allDefects.AddRange(analysis.MlDetectedDefects);
        }

        // Determine normalized image dimensions for region classification
        var width = 630;  // Default normalized width
        var height = 880; // Default normalized height

        return allDefects
            .Select(d =>
            {
                var region = regionSegmenter.ClassifyPoint(d.X, d.Y, width, height);
                return DefectReport.FromDefect(d, region);
            })
            .OrderByDescending(d => d.Severity)
            .ToList();
    }

}
