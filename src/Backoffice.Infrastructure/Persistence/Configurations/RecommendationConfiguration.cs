using System.Text.Json;
using Backoffice.Domain.Recommendations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backoffice.Infrastructure.Persistence.Configurations;

public sealed class RecommendationConfiguration : IEntityTypeConfiguration<Recommendation>
{
    public void Configure(EntityTypeBuilder<Recommendation> builder)
    {
        builder.ToTable("recommendations");
        builder.HasKey(r => r.RecommendationId);
        builder.Property(r => r.RecommendationId).ValueGeneratedNever();

        builder.Property(r => r.TenantId).IsRequired().HasMaxLength(128);
        builder.Property(r => r.Outcome).HasConversion<string>().HasMaxLength(64);
        builder.Property(r => r.Rationale).IsRequired().HasMaxLength(2048);
        builder.Property(r => r.CreatedBy).IsRequired().HasMaxLength(128);

        builder.Property(r => r.EvidenceReferences)
            .HasConversion(
                ids => JsonSerializer.Serialize(ids, (JsonSerializerOptions?)null),
                json => JsonSerializer.Deserialize<List<Guid>>(json, (JsonSerializerOptions?)null) ?? new List<Guid>())
            .Metadata.SetValueComparer(new ValueComparer<List<Guid>>(
                (a, b) => a!.SequenceEqual(b!),
                a => a.Aggregate(0, (hash, id) => HashCode.Combine(hash, id.GetHashCode())),
                a => a.ToList()));

        builder.Property(r => r.RuleReferences)
            .HasConversion(
                rules => JsonSerializer.Serialize(rules, (JsonSerializerOptions?)null),
                json => JsonSerializer.Deserialize<List<string>>(json, (JsonSerializerOptions?)null) ?? new List<string>())
            .Metadata.SetValueComparer(new ValueComparer<List<string>>(
                (a, b) => a!.SequenceEqual(b!),
                a => a.Aggregate(0, (hash, s) => HashCode.Combine(hash, s.GetHashCode())),
                a => a.ToList()));

        builder.HasIndex(r => new { r.TenantId, r.CaseId, r.RecommendationVersion });
    }
}
