using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokemonCardGrader.Domain.Entities;

namespace PokemonCardGrader.Infrastructure.Data.Configurations;

public sealed class ImageAnalysisRecordConfiguration : IEntityTypeConfiguration<ImageAnalysisRecord>
{
    public void Configure(EntityTypeBuilder<ImageAnalysisRecord> builder)
    {
        builder.ToTable("ImageAnalysisRecords");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.CardImageId).IsRequired();
        builder.Property(r => r.Source).HasConversion<int>().IsRequired();
        builder.Property(r => r.CreatedAt).IsRequired();

        // Index for the dominant query: "give me the latest record for this image".
        builder.HasIndex(r => new { r.CardImageId, r.CreatedAt })
            .HasDatabaseName("IX_ImageAnalysisRecords_CardImageId_CreatedAt");

        // The analysis result keeps the same nested-JSON shape it had when it
        // was an owned entity on CardImage — the structure is rich and it never
        // queries by sub-fields, so JSON storage is the right call.
        builder.OwnsOne(r => r.Result, ar =>
        {
            ar.ToJson();
            ar.OwnsOne(x => x.DetectedCentering);
            ar.OwnsMany(x => x.DetectedDefects);
            ar.OwnsMany(x => x.MlDetectedDefects);
            ar.OwnsOne(x => x.Overlay, o =>
            {
                o.OwnsMany(ov => ov.OuterGuides);
                o.OwnsMany(ov => ov.InnerGuides);
            });
            ar.OwnsOne(x => x.ConfidenceDetail);
            ar.OwnsOne(x => x.QualityAssessment);
            ar.OwnsOne(x => x.FailureDetection);
            ar.OwnsOne(x => x.Regions, reg =>
            {
                reg.OwnsOne(r => r.BorderRegion);
                reg.OwnsOne(r => r.ArtworkRegion);
                reg.OwnsOne(r => r.TextRegion);
                reg.OwnsMany(r => r.CornerZones);
                reg.OwnsMany(r => r.EdgeZones);
                reg.OwnsOne(r => r.InnerRegion);
            });
            ar.OwnsOne(x => x.Features);
        });
    }
}
