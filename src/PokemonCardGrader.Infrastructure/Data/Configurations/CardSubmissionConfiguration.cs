using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokemonCardGrader.Domain.Entities;

namespace PokemonCardGrader.Infrastructure.Data.Configurations;

public sealed class CardSubmissionConfiguration : IEntityTypeConfiguration<CardSubmission>
{
    public void Configure(EntityTypeBuilder<CardSubmission> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.UserId).HasMaxLength(450).IsRequired();
        builder.Property(e => e.Notes).HasMaxLength(2000);

        builder.HasIndex(e => e.UserId);

        builder.HasOne(e => e.PokemonCard)
            .WithMany()
            .HasForeignKey(e => e.PokemonCardId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.OwnsOne(e => e.ManualScores, scores =>
        {
            scores.ToJson();
            scores.OwnsOne(s => s.Centering);
        });

        builder.OwnsOne(e => e.ImageDerivedScores, scores =>
        {
            scores.ToJson();
            scores.OwnsOne(s => s.Centering);
        });

        builder.OwnsOne(e => e.FinalScores, scores =>
        {
            scores.ToJson();
            scores.OwnsOne(s => s.Centering);
        });

        builder.HasMany(e => e.Images)
            .WithOne()
            .HasForeignKey(e => e.CardSubmissionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.Estimates)
            .WithOne()
            .HasForeignKey(e => e.CardSubmissionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.ActualResult)
            .WithOne()
            .HasForeignKey<GradingResult>(e => e.CardSubmissionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(e => e.Images)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Navigation(e => e.Estimates)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
