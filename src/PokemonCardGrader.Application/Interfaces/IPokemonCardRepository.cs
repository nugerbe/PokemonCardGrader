using PokemonCardGrader.Domain.Entities;

namespace PokemonCardGrader.Application.Interfaces;

public interface IPokemonCardRepository
{
    Task<PokemonCard?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<PokemonCard?> GetByTcgApiIdAsync(string tcgApiId, CancellationToken ct = default);
    Task<List<PokemonCard>> SearchAsync(string query, int limit = 20, CancellationToken ct = default);
    Task<PokemonCard> AddAsync(PokemonCard card, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
