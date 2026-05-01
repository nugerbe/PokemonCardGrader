using Microsoft.AspNetCore.Identity;

namespace PokemonCardGrader.Infrastructure.Data;

public sealed class ApplicationUser : IdentityUser
{
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
