using PokemonCardGrader.Domain.Enums;

namespace PokemonCardGrader.Application.DTOs;

public sealed record ImageProcessingRequest
{
    public required Guid CardImageId { get; init; }
    public required Guid CardSubmissionId { get; init; }
    public required string StoragePath { get; init; }
    public required ImageType ImageType { get; init; }
}
