using Backoffice.Domain.Executions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backoffice.Infrastructure.Persistence.Configurations;

public sealed class ExecutionConfiguration : IEntityTypeConfiguration<Execution>
{
    public void Configure(EntityTypeBuilder<Execution> builder)
    {
        builder.ToTable("executions");
        builder.HasKey(e => e.ExecutionId);
        builder.Property(e => e.ExecutionId).ValueGeneratedNever();

        builder.Property(e => e.TenantId).IsRequired().HasMaxLength(128);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(64);
        builder.Property(e => e.IdempotencyKey).IsRequired().HasMaxLength(128);
        builder.Property(e => e.CommandHash).IsRequired().HasMaxLength(64);
        builder.Property(e => e.ExternalReference).HasMaxLength(256);

        builder.HasIndex(e => new { e.TenantId, e.CaseId });
    }
}

public sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("idempotency_records");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.TenantId).IsRequired().HasMaxLength(128);
        builder.Property(r => r.IdempotencyKey).IsRequired().HasMaxLength(128);
        builder.Property(r => r.CommandHash).IsRequired().HasMaxLength(64);

        builder.HasIndex(r => new { r.TenantId, r.CaseId, r.IdempotencyKey }).IsUnique();
    }
}
