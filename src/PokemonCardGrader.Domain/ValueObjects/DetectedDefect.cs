namespace PokemonCardGrader.Domain.ValueObjects;

public sealed record DetectedDefect
{
    public required string Type { get; init; }
    public required double Severity { get; init; }
    public required double X { get; init; }
    public required double Y { get; init; }
    public required double Width { get; init; }
    public required double Height { get; init; }
    public required double Confidence { get; init; }
}
