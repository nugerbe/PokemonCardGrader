namespace PokemonCardGrader.Application.DTOs;

public sealed record CardSetDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Series { get; init; }
    public required int TotalCards { get; init; }
    public string? LogoUrl { get; init; }
    public string? SymbolUrl { get; init; }
    public string? ReleaseDate { get; init; }
}
