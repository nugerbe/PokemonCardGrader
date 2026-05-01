using PokemonCardGrader.Application.DTOs;
using PokemonCardGrader.Application.Interfaces;
using PokemonCardGrader.Domain.Entities;
using PokemonCardGrader.Domain.Enums;

namespace PokemonCardGrader.Application.Services;

public sealed class CardLookupService(
    IPokemonTcgApiClient apiClient,
    IPokemonCardRepository cardRepository)
{
    public async Task<List<PokemonCardDto>> SearchCardsAsync(string query, int page = 1, CancellationToken ct = default)
    {
        return await apiClient.SearchCardsAsync(query, page, ct: ct);
    }

    public async Task<List<CardSetDto>> GetSetsAsync(CancellationToken ct = default)
    {
        return await apiClient.GetSetsAsync(ct);
    }

    public async Task<List<PokemonCardDto>> GetCardsBySetAsync(string setCode, int page = 1, CancellationToken ct = default)
    {
        return await apiClient.GetCardsBySetAsync(setCode, page, ct: ct);
    }

    public async Task<PokemonCard> GetOrCreateCardAsync(string tcgApiId, CancellationToken ct = default)
    {
        var existing = await cardRepository.GetByTcgApiIdAsync(tcgApiId, ct);
        if (existing is not null)
            return existing;

        var dto = await apiClient.GetCardByIdAsync(tcgApiId, ct)
            ?? throw new InvalidOperationException($"Card with API ID '{tcgApiId}' not found.");

        var card = PokemonCard.CreateFromApi(
            dto.TcgApiId,
            dto.Name,
            dto.SetName,
            dto.SetCode,
            dto.Number,
            ParseRarity(dto.Rarity),
            dto.ImageUrlLarge ?? dto.ImageUrlSmall);

        await cardRepository.AddAsync(card, ct);
        await cardRepository.SaveChangesAsync(ct);
        return card;
    }

    public async Task<PokemonCard> CreateManualCardAsync(
        string name, string setName, string setCode, string number, CardRarity rarity,
        CancellationToken ct = default)
    {
        var card = PokemonCard.CreateManual(name, setName, setCode, number, rarity);
        await cardRepository.AddAsync(card, ct);
        await cardRepository.SaveChangesAsync(ct);
        return card;
    }

    private static CardRarity ParseRarity(string rarity)
    {
        return rarity.ToLowerInvariant() switch
        {
            "common" => CardRarity.Common,
            "uncommon" => CardRarity.Uncommon,
            "rare" => CardRarity.Rare,
            "rare holo" => CardRarity.RareHolo,
            "rare ultra" => CardRarity.RareUltra,
            "rare secret" => CardRarity.RareSecret,
            "rare rainbow" => CardRarity.RareRainbow,
            "promo" => CardRarity.Promo,
            _ => CardRarity.Other
        };
    }
}
