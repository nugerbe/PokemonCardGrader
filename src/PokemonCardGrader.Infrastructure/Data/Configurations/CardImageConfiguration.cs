using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokemonCardGrader.Domain.Entities;

namespace PokemonCardGrader.Infrastructure.Data.Configurations;

public sealed class CardImageConfiguration : IEntityTypeConfiguration<CardImage>
{
    public void Configure(EntityTypeBuilder<CardImage> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.StoragePath).HasMaxLength(500).IsRequired();
        builder.Property(e => e.FileName).HasMaxLength(255).IsRequired();
        builder.Property(e => e.NormalizedStoragePath).HasMaxLength(500);

        // 1-to-many to ImageAnalysisRecord. EF discovers the backing field
        // (_analysisRecords) automatically because the navigation collection
        // is read-only; explicit configuration here makes the FK + cascade
        // behavior unambiguous.
        builder.HasMany(e => e.AnalysisRecords)
            .WithOne()
            .HasForeignKey(r => r.CardImageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(e => e.AnalysisRecords)
            .HasField("_analysisRecords")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
