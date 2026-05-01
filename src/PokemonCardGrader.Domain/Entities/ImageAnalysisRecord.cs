using PokemonCardGrader.Domain.Enums;
using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Domain.Entities;

/// <summary>
/// One run of the analysis pipeline for a <see cref="CardImage"/>, persisted
/// as its own row in the <c>ImageAnalysisRecords</c> table. Records are
/// append-only: every fresh analysis or user correction inserts a new row,
/// and the "current" analysis for an image is the row with the latest
/// <see cref="CreatedAt"/>. Older rows are retained as audit trail and as
/// training data for the learning pipeline (the diff between an Initial
/// row and a subsequent UserCorrection row IS the supervised label —
/// "what we said" vs "what the human said").
/// </summary>
public sealed class ImageAnalysisRecord
{
    public Guid Id { get; private set; }
    public Guid CardImageId { get; private set; }
    public ImageAnalysisResult Result { get; private set; } = null!;
    public AnalysisRecordSource Source { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private ImageAnalysisRecord() { }

    public static ImageAnalysisRecord Create(
        Guid cardImageId,
        ImageAnalysisResult result,
        AnalysisRecordSource source) =>
        new()
        {
            Id = Guid.NewGuid(),
            CardImageId = cardImageId,
            Result = result,
            Source = source,
            CreatedAt = DateTimeOffset.UtcNow
        };
}
