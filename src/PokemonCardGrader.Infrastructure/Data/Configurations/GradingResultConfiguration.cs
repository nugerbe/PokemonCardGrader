using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokemonCardGrader.Domain.Entities;

namespace PokemonCardGrader.Infrastructure.Data.Configurations;

public sealed class GradingResultConfiguration : IEntityTypeConfiguration<GradingResult>
{
    public void Configure(EntityTypeBuilder<GradingResult> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.UserId).HasMaxLength(450).IsRequired();
        builder.Property(e => e.CertificationNumber).HasMaxLength(50);

        builder.HasIndex(e => e.CardSubmissionId).IsUnique();
        builder.HasIndex(e => new { e.Company, e.ActualGrade });

        var dictionaryComparer = new ValueComparer<Dictionary<string, double>>(
            (a, b) => a != null && b != null && a.SequenceEqual(b),
            v => v.Aggregate(0, (hash, kvp) => HashCode.Combine(hash, kvp.Key, kvp.Value)),
            v => new Dictionary<string, double>(v));

        builder.Property(e => e.ActualSubGrades)
            .HasColumnType("nvarchar(max)")
            .HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, double>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new())
            .Metadata.SetValueComparer(dictionaryComparer);
    }
}
