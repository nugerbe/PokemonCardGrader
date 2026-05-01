namespace PokemonCardGrader.Application.DTOs;

public sealed record PokemonCardDto
{
    public required string TcgApiId { get; init; }
    public required string Name { get; init; }
    public required string SetName { get; init; }
    public required string SetCode { get; init; }
    public required string Number { get; init; }
    public required string Rarity { get; init; }
    public string? ImageUrlSmall { get; init; }
    public string? ImageUrlLarge { get; init; }
    public string? Artist { get; init; }
}
