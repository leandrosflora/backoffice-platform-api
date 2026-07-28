using System.Text.Json;
using Backoffice.Domain.Investigations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backoffice.Infrastructure.Persistence.Configurations;

public sealed class InvestigationConfiguration : IEntityTypeConfiguration<Investigation>
{
    public void Configure(EntityTypeBuilder<Investigation> builder)
    {
        builder.ToTable("investigations");
        builder.HasKey(i => i.InvestigationId);
        builder.Property(i => i.InvestigationId).ValueGeneratedNever();

        builder.Property(i => i.TenantId).IsRequired().HasMaxLength(128);
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(64);

        // Findings is a small, self-contained list with no independent identity or query
        // needs of its own, so it is stored as a single JSON column rather than a child table.
        builder.Property(i => i.Findings)
            .HasConversion(
                findings => JsonSerializer.Serialize(findings, (JsonSerializerOptions?)null),
                json => JsonSerializer.Deserialize<List<Finding>>(json, (JsonSerializerOptions?)null) ?? new List<Finding>())
            .Metadata.SetValueComparer(new ValueComparer<List<Finding>>(
                (a, b) => a!.SequenceEqual(b!),
                a => a.Aggregate(0, (hash, f) => HashCode.Combine(hash, f.GetHashCode())),
                a => a.ToList()));

        builder.HasIndex(i => new { i.TenantId, i.CaseId });
    }
}
