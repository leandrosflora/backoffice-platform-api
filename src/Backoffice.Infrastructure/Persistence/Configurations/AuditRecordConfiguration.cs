using System.Text.Json;
using Backoffice.Domain.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backoffice.Infrastructure.Persistence.Configurations;

public sealed class AuditRecordConfiguration : IEntityTypeConfiguration<AuditRecord>
{
    public void Configure(EntityTypeBuilder<AuditRecord> builder)
    {
        builder.ToTable("audit_records");
        builder.HasKey(r => r.Id);

        builder.HasIndex(r => r.EventId).IsUnique();
        builder.Property(r => r.EventType).IsRequired().HasMaxLength(128);
        builder.Property(r => r.TenantId).IsRequired().HasMaxLength(128);
        builder.Property(r => r.PolicyAction).HasMaxLength(128);

        builder.Property(r => r.RuleReferences)
            .HasConversion(
                rules => JsonSerializer.Serialize(rules, (JsonSerializerOptions?)null),
                json => JsonSerializer.Deserialize<List<string>>(json, (JsonSerializerOptions?)null) ?? new List<string>())
            .Metadata.SetValueComparer(new ValueComparer<IReadOnlyList<string>>(
                (a, b) => a!.SequenceEqual(b!),
                a => a.Aggregate(0, (hash, s) => HashCode.Combine(hash, s.GetHashCode())),
                a => a.ToList()));

        builder.HasIndex(r => new { r.TenantId, r.OccurredAt });
    }
}
