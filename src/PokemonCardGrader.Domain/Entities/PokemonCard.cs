using PokemonCardGrader.Domain.Enums;

namespace PokemonCardGrader.Domain.Entities;

public sealed class PokemonCard
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? TcgApiId { get; private set; }
    public string SetName { get; private set; } = string.Empty;
    public string SetCode { get; private set; } = string.Empty;
    public string Number { get; private set; } = string.Empty;
    public CardRarity Rarity { get; private set; }
    public string? ImageUrl { get; private set; }
    public bool IsManualEntry { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private PokemonCard() { }

    public static PokemonCard CreateFromApi(
        string tcgApiId,
        string name,
        string setName,
        string setCode,
        string number,
        CardRarity rarity,
        string? imageUrl)
    {
        return new PokemonCard
        {
            Id = Guid.NewGuid(),
            TcgApiId = tcgApiId,
            Name = name,
            SetName = setName,
            SetCode = setCode,
            Number = number,
            Rarity = rarity,
            ImageUrl = imageUrl,
            IsManualEntry = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public static PokemonCard CreateManual(
        string name,
        string setName,
        string setCode,
        string number,
        CardRarity rarity)
    {
        return new PokemonCard
        {
            Id = Guid.NewGuid(),
            Name = name,
            SetName = setName,
            SetCode = setCode,
            Number = number,
            Rarity = rarity,
            IsManualEntry = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
