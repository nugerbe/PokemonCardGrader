using Microsoft.EntityFrameworkCore;
using PokemonCardGrader.Application.Interfaces;
using PokemonCardGrader.Domain.Entities;
using PokemonCardGrader.Infrastructure.Data;

namespace PokemonCardGrader.Infrastructure.Repositories;

public sealed class PokemonCardRepository(ApplicationDbContext db) : IPokemonCardRepository
{
    public async Task<PokemonCard?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await db.PokemonCards.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task<PokemonCard?> GetByTcgApiIdAsync(string tcgApiId, CancellationToken ct = default)
    {
        return await db.PokemonCards.AsNoTracking().FirstOrDefaultAsync(c => c.TcgApiId == tcgApiId, ct);
    }

    public async Task<List<PokemonCard>> SearchAsync(string query, int limit = 20, CancellationToken ct = default)
    {
        return await db.PokemonCards.AsNoTracking()
            .Where(c => c.Name.Contains(query) || c.SetName.Contains(query))
            .OrderBy(c => c.Name)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<PokemonCard> AddAsync(PokemonCard card, CancellationToken ct = default)
    {
        await db.PokemonCards.AddAsync(card, ct);
        return card;
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await db.SaveChangesAsync(ct);
    }
}
