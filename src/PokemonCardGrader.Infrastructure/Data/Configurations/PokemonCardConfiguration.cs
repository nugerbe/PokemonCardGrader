using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokemonCardGrader.Domain.Entities;

namespace PokemonCardGrader.Infrastructure.Data.Configurations;

public sealed class PokemonCardConfiguration : IEntityTypeConfiguration<PokemonCard>
{
    public void Configure(EntityTypeBuilder<PokemonCard> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.TcgApiId).HasMaxLength(50);
        builder.Property(e => e.SetName).HasMaxLength(200);
        builder.Property(e => e.SetCode).HasMaxLength(20);
        builder.Property(e => e.Number).HasMaxLength(20);
        builder.Property(e => e.ImageUrl).HasMaxLength(500);

        builder.HasIndex(e => e.TcgApiId)
            .IsUnique()
            .HasFilter("[TcgApiId] IS NOT NULL");

        builder.HasIndex(e => e.SetCode);
    }
}
