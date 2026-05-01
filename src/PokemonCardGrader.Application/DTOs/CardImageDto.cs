using PokemonCardGrader.Domain.Enums;
using PokemonCardGrader.Domain.ValueObjects;

namespace PokemonCardGrader.Application.DTOs;

public sealed record CardImageDto
{
    public required Guid Id { get; init; }
    public required string ImageUrl { get; init; }
    public required ImageType ImageType { get; init; }
    public bool IsAnalyzed { get; init; }
    public AnalysisOverlay? Overlay { get; init; }
    public List<DetectedDefect>? DetectedDefects { get; init; }
    public DateTimeOffset UploadedAt { get; init; }

    /// <summary>
    /// URL of the rectified (perspective-corrected) card image, when analysis has
    /// produced one. Used by the centering overlay editor as the primary surface
    /// for the digital grading template — see <c>ImageOverlayEditor</c>.
    /// Null until analysis completes successfully.
    /// </summary>
    public string? NormalizedImageUrl { get; init; }
}
