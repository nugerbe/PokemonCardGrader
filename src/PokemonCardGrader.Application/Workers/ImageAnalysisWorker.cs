using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PokemonCardGrader.Application.DTOs;
using PokemonCardGrader.Application.Interfaces;
using PokemonCardGrader.Application.Services;
using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Application.Workers;

public sealed class ImageAnalysisWorker(
    Channel<ImageProcessingRequest> channel,
    IServiceScopeFactory scopeFactory,
    ILogger<ImageAnalysisWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("ImageAnalysisWorker started.");

        await foreach (var request in channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessImageAsync(request, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing image {ImageId}.", request.CardImageId);
            }
        }

        logger.LogInformation("ImageAnalysisWorker stopped.");
    }

    private async Task ProcessImageAsync(ImageProcessingRequest request, CancellationToken ct)
    {
        logger.LogInformation("Analyzing image {ImageId} for submission {SubmissionId}.",
            request.CardImageId, request.CardSubmissionId);

        // Scope 1 — analyse image and persist result on CardImage.
        // Uses its own DbContext so tracked entities don't leak into scope 2.
        ImageAnalysisResult result;
        await using (var scope1 = scopeFactory.CreateAsyncScope())
        {
            var storageService = scope1.ServiceProvider.GetRequiredService<IImageStorageService>();
            var analysisService = scope1.ServiceProvider.GetRequiredService<IImageAnalysisService>();
            var submissionRepository = scope1.ServiceProvider.GetRequiredService<ICardSubmissionRepository>();

            var imageStream = await storageService.GetImageAsync(request.StoragePath, ct);
            if (imageStream is null)
            {
                logger.LogWarning("Image not found at path {Path}.", request.StoragePath);
                return;
            }

            await using (imageStream)
            {
                result = await analysisService.AnalyzeImageAsync(imageStream, request.ImageType, ct);
            }

            logger.LogInformation(
                "Analysis complete for image {ImageId}. Method: {Method}",
                request.CardImageId, result.AnalysisMethod);

            var cardImage = await submissionRepository.GetImageByIdAsync(request.CardImageId, ct);
            if (cardImage is null)
            {
                logger.LogWarning("CardImage {ImageId} not found.", request.CardImageId);
                return;
            }

            cardImage.SetAnalysisResult(result);
            await submissionRepository.SaveChangesAsync(ct);
        }

        // Scope 2 — combine ALL analyzed images for this submission and set scores.
        // A separate scope guarantees a clean DbContext with no previously tracked
        // entities (CardImage + its ToJson AnalysisResult) that could cause
        // spurious UPDATEs on unrelated entities.
        //
        // CombineAndSetImageScoresAsync loads all analyzed images, merges front/back
        // centering + condition scores, and persists the combined result. This ensures
        // image 2 doesn't overwrite image 1's scores — the combination is always
        // recalculated from all available data.
        await using var scope2 = scopeFactory.CreateAsyncScope();
        var service = scope2.ServiceProvider.GetRequiredService<CardSubmissionService>();
        await service.CombineAndSetImageScoresAsync(request.CardSubmissionId, ct);

        logger.LogInformation(
            "Combined image scores persisted for submission {SubmissionId}.",
            request.CardSubmissionId);
    }
}
