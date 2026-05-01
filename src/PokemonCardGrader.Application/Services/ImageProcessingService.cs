using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using PokemonCardGrader.Application.DTOs;
using PokemonCardGrader.Application.Interfaces;
using PokemonCardGrader.Domain.Entities;
using PokemonCardGrader.Domain.Enums;

namespace PokemonCardGrader.Application.Services;

public sealed class ImageProcessingService(
    IImageStorageService storageService,
    IServiceScopeFactory scopeFactory,
    Channel<ImageProcessingRequest> processingChannel)
{
    public async Task<CardImage> UploadAndEnqueueAsync(
        Guid submissionId,
        string userId,
        Stream imageStream,
        string fileName,
        ImageType imageType,
        long fileSizeBytes,
        CancellationToken ct = default)
    {
        // Use a dedicated scope so the SaveChangesAsync below only touches the new
        // CardImage — NOT the CardSubmission that is tracked in the Blazor circuit's
        // scoped DbContext.  Without this isolation, EF change detection can
        // spuriously UPDATE the CardSubmission during the image INSERT.
        await using var scope = scopeFactory.CreateAsyncScope();
        var submissionRepository = scope.ServiceProvider.GetRequiredService<ICardSubmissionRepository>();

        if (!await submissionRepository.ExistsAsync(submissionId, userId, ct))
            throw new InvalidOperationException("Submission not found.");

        var storagePath = await storageService.SaveImageAsync(imageStream, fileName, ct);

        var cardImage = CardImage.Create(submissionId, storagePath, fileName, imageType, fileSizeBytes);
        await submissionRepository.AddImageAsync(cardImage, ct);
        await submissionRepository.SaveChangesAsync(ct);

        var request = new ImageProcessingRequest
        {
            CardImageId = cardImage.Id,
            CardSubmissionId = submissionId,
            StoragePath = storagePath,
            ImageType = imageType
        };

        await processingChannel.Writer.WriteAsync(request, ct);

        return cardImage;
    }
}
