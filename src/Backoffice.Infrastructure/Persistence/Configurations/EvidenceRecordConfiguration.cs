using Backoffice.Domain.Evidence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backoffice.Infrastructure.Persistence.Configurations;

public sealed class EvidenceRecordConfiguration : IEntityTypeConfiguration<EvidenceRecord>
{
    public void Configure(EntityTypeBuilder<EvidenceRecord> builder)
    {
        builder.ToTable("evidence");
        builder.HasKey(e => e.EvidenceId);
        builder.Property(e => e.EvidenceId).ValueGeneratedNever();

        builder.Property(e => e.TenantId).IsRequired().HasMaxLength(128);
        builder.Property(e => e.EvidenceType).HasConversion<string>().HasMaxLength(64);
        builder.Property(e => e.SourceType).HasConversion<string>().HasMaxLength(64);
        builder.Property(e => e.SourceReference).IsRequired().HasMaxLength(512);
        builder.Property(e => e.SourceVersion).IsRequired().HasMaxLength(128);
        builder.Property(e => e.Position).HasMaxLength(256);
        builder.Property(e => e.Checksum).HasMaxLength(64);

        builder.HasIndex(e => new { e.TenantId, e.CaseId });
    }
}
