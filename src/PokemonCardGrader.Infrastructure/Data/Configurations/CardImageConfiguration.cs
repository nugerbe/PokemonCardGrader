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

        builder.OwnsOne(e => e.AnalysisResult, ar =>
        {
            ar.ToJson();
            ar.OwnsOne(r => r.DetectedCentering);
            ar.OwnsMany(r => r.DetectedDefects);
            ar.OwnsMany(r => r.MlDetectedDefects);
            ar.OwnsOne(r => r.Overlay, o =>
            {
                o.OwnsMany(ov => ov.CardBoundary);
                o.OwnsOne(ov => ov.BorderLines);
            });
            ar.OwnsOne(r => r.ConfidenceDetail);
            ar.OwnsOne(r => r.QualityAssessment);
            ar.OwnsOne(r => r.FailureDetection);
            ar.OwnsOne(r => r.Regions, reg =>
            {
                reg.OwnsOne(r => r.BorderRegion);
                reg.OwnsOne(r => r.ArtworkRegion);
                reg.OwnsOne(r => r.TextRegion);
                reg.OwnsMany(r => r.CornerZones);
                reg.OwnsMany(r => r.EdgeZones);
                reg.OwnsOne(r => r.InnerRegion);
            });
            ar.OwnsOne(r => r.Features);
        });
    }
}
