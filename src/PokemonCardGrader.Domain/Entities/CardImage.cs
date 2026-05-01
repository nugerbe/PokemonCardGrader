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

    /// <summary>
    /// Storage path of the perspective-corrected (rectified) card image produced
    /// during analysis. When set, the client renders this as the primary view in
    /// the centering overlay editor — equivalent to the user laying the card flat
    /// under a transparent grading template. Null until analysis completes.
    /// </summary>
    public string? NormalizedStoragePath { get; private set; }

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

    public void SetNormalizedStoragePath(string? path)
    {
        NormalizedStoragePath = string.IsNullOrWhiteSpace(path) ? null : path;
    }
}
