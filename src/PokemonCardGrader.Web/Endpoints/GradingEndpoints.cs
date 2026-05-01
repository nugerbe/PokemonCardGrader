using Microsoft.AspNetCore.Mvc;
using PokemonCardGrader.Application.Interfaces;
using PokemonCardGrader.Domain.Enums;
using PokemonCardGrader.Domain.ValueObjects;
using PokemonCardGrader.Infrastructure.ImageAnalysis;
using PokemonCardGrader.Infrastructure.ML;

namespace PokemonCardGrader.Web.Endpoints;

/// <summary>
/// Phase 23: Minimal API endpoints for image submission, grading retrieval,
/// and debug output. Designed for programmatic access alongside the Blazor UI.
/// </summary>
public static class GradingEndpoints
{
    public static void MapGradingEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/grading")
            .WithTags("Grading");

        group.MapPost("/analyze", AnalyzeImage)
            .DisableAntiforgery()
            .Produces<AnalysisResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)
            .WithName("AnalyzeImage")
            .WithSummary("Submit a card image for analysis");

        group.MapGet("/result/{submissionId:guid}", GetResult)
            .Produces<FinalGradingOutput>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .WithName("GetGradingResult")
            .WithSummary("Retrieve a grading result by submission ID");

        group.MapGet("/health", GetHealth)
            .Produces<HealthResponse>(StatusCodes.Status200OK)
            .WithName("GradingHealth")
            .WithSummary("Pipeline health and model status");

        group.MapPost("/feedback", SubmitFeedback)
            .DisableAntiforgery()
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .WithName("SubmitFeedback")
            .WithSummary("Submit grade feedback for calibration");

        group.MapGet("/calibration/{company}", GetCalibration)
            .Produces<CalibrationMetrics>(StatusCodes.Status200OK)
            .WithName("GetCalibration")
            .WithSummary("Get calibration metrics for a grading company");

        group.MapGet("/evaluation", GetEvaluation)
            .Produces<EvaluationMetrics>(StatusCodes.Status200OK)
            .WithName("GetEvaluation")
            .WithSummary("Get latest evaluation metrics");
    }

    private static async Task<IResult> AnalyzeImage(
        IFormFile? file,
        [FromQuery] string? imageType,
        IImageAnalysisService analysisService,
        ImageQualityAnalyzer qualityAnalyzer,
        FailureDetector failureDetector,
        ILogger<Program> logger)
    {
        if (file is null || file.Length == 0)
        {
            return Results.Problem(
                detail: "No image file provided.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (file.Length > 20 * 1024 * 1024) // 20MB limit
        {
            return Results.Problem(
                detail: "Image file exceeds 20MB limit.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var type = imageType?.Equals("back", StringComparison.OrdinalIgnoreCase) == true
            ? ImageType.Back
            : ImageType.Front;

        try
        {
            await using var stream = file.OpenReadStream();
            var outcome = await analysisService.AnalyzeImageAsync(stream, type);
            var result = outcome.Result;

            var response = new AnalysisResponse(
                SubmissionId: Guid.NewGuid(),
                AnalyzedAt: result.AnalyzedAt,
                Method: result.AnalysisMethod,
                CornersScore: result.CornersScore,
                EdgesScore: result.EdgesScore,
                SurfaceScore: result.SurfaceScore,
                Centering: result.DetectedCentering,
                DefectCount: result.DetectedDefects.Count + result.MlDetectedDefects.Count,
                Confidence: result.OverallConfidence ?? 0,
                ImageQualityScore: result.ImageQualityScore,
                ConfidenceDetail: result.ConfidenceDetail);

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Image analysis failed.");
            return Results.Problem(
                detail: "Analysis failed. Please try again with a different image.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }
    }

    private static IResult GetResult(
        Guid submissionId,
        ILogger<Program> logger)
    {
        // In a full implementation, this would look up the result from a persistence store.
        // For now, return 404 — the Blazor UI handles result retrieval via the database.
        logger.LogInformation("Result requested for submission {Id}", submissionId);
        return Results.NotFound();
    }

    private static IResult GetHealth(
        IMLModelRegistry modelRegistry,
        EvaluationService evaluationService)
    {
        var models = modelRegistry.GetAll();
        var latestEval = evaluationService.LoadLatestMetrics();

        var response = new HealthResponse(
            Status: "healthy",
            RegisteredModels: models.Count,
            ReadyModels: models.Count(m => m.IsReady),
            ModelPurposes: models.Select(m => m.Purpose.ToString()).Distinct().ToList(),
            LatestEvaluation: latestEval);

        return Results.Ok(response);
    }

    private static IResult SubmitFeedback(
        FeedbackRequest request,
        UserFeedbackService feedbackService)
    {
        if (request.SubmissionId == Guid.Empty)
        {
            return Results.Problem(
                detail: "SubmissionId is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.ActualGrade is < 1 or > 10)
        {
            return Results.Problem(
                detail: "ActualGrade must be between 1 and 10.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        feedbackService.RecordGradeOverride(
            request.SubmissionId,
            request.PredictedGrade,
            request.ActualGrade,
            request.Company ?? "unknown",
            request.AnalysisMethod ?? "unknown",
            request.Confidence,
            request.Notes);

        return Results.NoContent();
    }

    private static IResult GetCalibration(
        string company,
        CalibrationService calibrationService)
    {
        var metrics = calibrationService.ComputeMetrics(company);
        return Results.Ok(metrics);
    }

    private static IResult GetEvaluation(EvaluationService evaluationService)
    {
        var metrics = evaluationService.LoadLatestMetrics();
        return metrics is not null
            ? Results.Ok(metrics)
            : Results.Ok(new { message = "No evaluation data available yet." });
    }

    // ── Request/Response DTOs ──

    public sealed record AnalysisResponse(
        Guid SubmissionId,
        DateTimeOffset AnalyzedAt,
        string Method,
        double? CornersScore,
        double? EdgesScore,
        double? SurfaceScore,
        CenteringMeasurement? Centering,
        int DefectCount,
        double Confidence,
        double? ImageQualityScore,
        ConfidenceBreakdown? ConfidenceDetail);

    public sealed record HealthResponse(
        string Status,
        int RegisteredModels,
        int ReadyModels,
        List<string> ModelPurposes,
        EvaluationMetrics? LatestEvaluation);

    public sealed record FeedbackRequest(
        Guid SubmissionId,
        double PredictedGrade,
        double ActualGrade,
        string? Company,
        string? AnalysisMethod,
        double Confidence,
        string? Notes);
}
