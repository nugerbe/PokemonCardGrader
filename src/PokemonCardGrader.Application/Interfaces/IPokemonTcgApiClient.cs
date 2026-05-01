using PokemonCardGrader.Application.DTOs;

namespace PokemonCardGrader.Application.Interfaces;

public interface IPokemonTcgApiClient
{
    Task<List<PokemonCardDto>> SearchCardsAsync(string query, int page = 1, int pageSize = 20, CancellationToken ct = default);
    Task<PokemonCardDto?> GetCardByIdAsync(string tcgApiId, CancellationToken ct = default);
    Task<List<CardSetDto>> GetSetsAsync(CancellationToken ct = default);
    Task<List<PokemonCardDto>> GetCardsBySetAsync(string setCode, int page = 1, int pageSize = 20, CancellationToken ct = default);
}
