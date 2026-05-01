using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PokemonCardGrader.Domain.Entities;

namespace PokemonCardGrader.Infrastructure.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<PokemonCard> PokemonCards => Set<PokemonCard>();
    public DbSet<CardSubmission> CardSubmissions => Set<CardSubmission>();
    public DbSet<CardImage> CardImages => Set<CardImage>();
    public DbSet<GradeEstimate> GradeEstimates => Set<GradeEstimate>();
    public DbSet<GradingResult> GradingResults => Set<GradingResult>();
    public DbSet<AnalysisCorrection> AnalysisCorrections => Set<AnalysisCorrection>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}
