using PokemonCardGrader.Domain.Enums;
using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Domain.Entities;

public sealed class CardImage
{
    public Guid Id { get; private set; }
    public Guid CardSubmissionId { get; private set; }
    public string StoragePath { get; private set; } = string.Empty;
    public string FileName { get; private set; } = string.Empty;
    public ImageType ImageType { get; private set; }
    public long FileSizeBytes { get; private set; }
    public ImageAnalysisResult? AnalysisResult { get; private set; }
    public DateTimeOffset UploadedAt { get; private set; }

    private CardImage() { }

    public static CardImage Create(
        Guid cardSubmissionId,
        string storagePath,
        string fileName,
        ImageType imageType,
        long fileSizeBytes)
    {
        return new CardImage
        {
            Id = Guid.NewGuid(),
            CardSubmissionId = cardSubmissionId,
            StoragePath = storagePath,
            FileName = fileName,
            ImageType = imageType,
            FileSizeBytes = fileSizeBytes,
            UploadedAt = DateTimeOffset.UtcNow
        };
    }

    public void SetAnalysisResult(ImageAnalysisResult result)
    {
        AnalysisResult = result;
    }
}
