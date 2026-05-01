using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokemonCardGrader.Domain.Entities;

namespace PokemonCardGrader.Infrastructure.Data.Configurations;

public sealed class AnalysisCorrectionConfiguration : IEntityTypeConfiguration<AnalysisCorrection>
{
    public void Configure(EntityTypeBuilder<AnalysisCorrection> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.CardImageId).IsRequired();
        builder.Property(e => e.CardSubmissionId).IsRequired();

        builder.OwnsOne(e => e.OriginalOverlay, o =>
        {
            o.ToJson();
            o.OwnsMany(ov => ov.CardBoundary);
            o.OwnsOne(ov => ov.BorderLines);
        });

        builder.OwnsOne(e => e.OriginalScores, s =>
        {
            s.ToJson();
            s.OwnsOne(sc => sc.Centering);
        });

        builder.OwnsOne(e => e.Correction, c =>
        {
            c.ToJson();
            c.OwnsOne(cr => cr.AdjustedBorders);
            c.OwnsMany(cr => cr.AdjustedBoundary);
        });

        builder.OwnsOne(e => e.CorrectedScores, s =>
        {
            s.ToJson();
            s.OwnsOne(sc => sc.Centering);
        });

        builder.HasIndex(e => e.CardSubmissionId);
        builder.HasIndex(e => e.CreatedAt);
    }
}
