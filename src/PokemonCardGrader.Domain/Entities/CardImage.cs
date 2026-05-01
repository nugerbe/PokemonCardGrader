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
    public DateTimeOffset UploadedAt { get; private set; }

    /// <summary>
    /// All analysis records for this image, append-only. The "current" analysis
    /// is the one with the latest <see cref="ImageAnalysisRecord.CreatedAt"/> —
    /// see <see cref="LatestAnalysis"/>. Repositories that load images for read
    /// should filter-include only the latest record; this collection just
    /// reflects whatever EF has materialised into memory.
    /// </summary>
    private readonly List<ImageAnalysisRecord> _analysisRecords = [];
    public IReadOnlyCollection<ImageAnalysisRecord> AnalysisRecords => _analysisRecords;

    public ImageAnalysisRecord? LatestAnalysis =>
        _analysisRecords.Count == 0
            ? null
            : _analysisRecords.MaxBy(r => r.CreatedAt);

    /// <summary>Convenience accessor for callers that want the analysis payload directly.</summary>
    public ImageAnalysisResult? LatestAnalysisResult => LatestAnalysis?.Result;

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

    /// <summary>
    /// TEST-ONLY: attach a pre-built analysis record into the in-memory
    /// navigation collection, mimicking what EF Core does when materialising
    /// the entity from the database. Production code must go through the
    /// repository's <c>AddAnalysisRecordAsync</c> instead — append-only
    /// records are persisted via the DbSet, not via this collection.
    /// </summary>
    internal void LoadAnalysisRecordForTest(ImageAnalysisRecord record)
    {
        _analysisRecords.Add(record);
    }
}
