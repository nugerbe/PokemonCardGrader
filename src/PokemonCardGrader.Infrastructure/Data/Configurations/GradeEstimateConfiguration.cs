using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokemonCardGrader.Domain.Entities;

namespace PokemonCardGrader.Infrastructure.Data.Configurations;

public sealed class GradeEstimateConfiguration : IEntityTypeConfiguration<GradeEstimate>
{
    public void Configure(EntityTypeBuilder<GradeEstimate> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Label).HasMaxLength(100);

        builder.HasIndex(e => new { e.CardSubmissionId, e.Company });

        var dictionaryComparer = new ValueComparer<Dictionary<string, double>>(
            (a, b) => a != null && b != null && a.SequenceEqual(b),
            v => v.Aggregate(0, (hash, kvp) => HashCode.Combine(hash, kvp.Key, kvp.Value)),
            v => new Dictionary<string, double>(v));

        builder.Property(e => e.SubGrades)
            .HasColumnType("nvarchar(max)")
            .HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, double>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new())
            .Metadata.SetValueComparer(dictionaryComparer);
    }
}
