using System.Text.Json;
using Backoffice.Domain.Cases;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backoffice.Infrastructure.Persistence.Configurations;

public sealed class TimelineEntryConfiguration : IEntityTypeConfiguration<TimelineEntry>
{
    public void Configure(EntityTypeBuilder<TimelineEntry> builder)
    {
        builder.ToTable("timeline");
        builder.HasKey(t => t.Id);
        // Id is always assigned client-side (TimelineEntry.Create), never store-generated;
        // without this, a new entry added only via the Case.Timeline collection navigation
        // (not an explicit DbSet.Add) gets misdetected as an existing row and EF emits an
        // UPDATE instead of an INSERT, which matches zero rows and throws a concurrency error.
        builder.Property(t => t.Id).ValueGeneratedNever();

        builder.Property(t => t.EventType).IsRequired().HasMaxLength(128);
        builder.Property(t => t.ActorId).IsRequired().HasMaxLength(128);
        builder.Property(t => t.Origin).IsRequired().HasMaxLength(128);
        builder.Property(t => t.Reason).IsRequired().HasMaxLength(1024);
        builder.Property(t => t.PolicyAction).HasMaxLength(128);

        builder.Property(t => t.RuleReferences)
            .HasConversion(
                rules => JsonSerializer.Serialize(rules, (JsonSerializerOptions?)null),
                json => JsonSerializer.Deserialize<List<string>>(json, (JsonSerializerOptions?)null) ?? new List<string>())
            .Metadata.SetValueComparer(new ValueComparer<IReadOnlyList<string>>(
                (a, b) => a!.SequenceEqual(b!),
                a => a.Aggregate(0, (hash, s) => HashCode.Combine(hash, s.GetHashCode())),
                a => a.ToList()));

        builder.HasIndex(t => new { t.CaseId, t.CaseVersion });
    }
}
