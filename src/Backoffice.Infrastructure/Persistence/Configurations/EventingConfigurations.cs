using Backoffice.Domain.Eventing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backoffice.Infrastructure.Persistence.Configurations;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox");
        builder.HasKey(m => m.Id);
        // Id is a database-generated bigint identity (unlike the Guid PKs elsewhere), so the
        // EF Core default (ValueGeneratedOnAdd) is correct and is left unset here.

        builder.HasIndex(m => m.EventId).IsUnique();
        builder.Property(m => m.TenantId).IsRequired().HasMaxLength(128);
        builder.Property(m => m.EventType).IsRequired().HasMaxLength(128);
        builder.Property(m => m.Topic).IsRequired().HasMaxLength(256);
        builder.Property(m => m.MessageKey).IsRequired().HasMaxLength(128);
        builder.Property(m => m.Producer).IsRequired().HasMaxLength(128);
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(m => m.LastError).HasMaxLength(2000);

        builder.HasIndex(m => new { m.Status, m.AvailableAt, m.Id });
        builder.HasIndex(m => m.TenantId);
    }
}

public sealed class InboxRecordConfiguration : IEntityTypeConfiguration<InboxRecord>
{
    public void Configure(EntityTypeBuilder<InboxRecord> builder)
    {
        builder.ToTable("inbox");
        builder.HasKey(r => new { r.ConsumerName, r.EventId });
        builder.Property(r => r.ConsumerName).IsRequired().HasMaxLength(128);
    }
}

public sealed class EventTimerConfiguration : IEntityTypeConfiguration<EventTimer>
{
    public void Configure(EntityTypeBuilder<EventTimer> builder)
    {
        builder.ToTable("timers");
        builder.HasKey(t => t.TimerId);
        builder.Property(t => t.TimerId).ValueGeneratedNever();

        builder.Property(t => t.TenantId).IsRequired().HasMaxLength(128);
        builder.Property(t => t.TimerType).IsRequired().HasMaxLength(64);
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(t => t.LastError).HasMaxLength(2000);

        builder.HasIndex(t => new { t.Status, t.DueAt });
        builder.HasIndex(t => t.TenantId);
    }
}

public sealed class DeadLetterConfiguration : IEntityTypeConfiguration<DeadLetter>
{
    public void Configure(EntityTypeBuilder<DeadLetter> builder)
    {
        builder.ToTable("dead_letters");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Source).IsRequired().HasMaxLength(32);
        builder.Property(d => d.SourceTopic).IsRequired().HasMaxLength(256);
        builder.Property(d => d.TenantId).IsRequired().HasMaxLength(128);
        builder.Property(d => d.EventType).IsRequired().HasMaxLength(128);
        builder.Property(d => d.Error).HasMaxLength(2000);
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(32);

        builder.HasIndex(d => new { d.TenantId, d.Status });
    }
}

public sealed class ReplayAuditEntryConfiguration : IEntityTypeConfiguration<ReplayAuditEntry>
{
    public void Configure(EntityTypeBuilder<ReplayAuditEntry> builder)
    {
        builder.ToTable("replay_audit");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.TenantId).IsRequired().HasMaxLength(128);
        builder.Property(r => r.ActorId).IsRequired().HasMaxLength(128);
        builder.Property(r => r.Reason).IsRequired().HasMaxLength(500);
    }
}
