using Backoffice.Domain.Approvals;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backoffice.Infrastructure.Persistence.Configurations;

public sealed class ApprovalConfiguration : IEntityTypeConfiguration<Approval>
{
    public void Configure(EntityTypeBuilder<Approval> builder)
    {
        builder.ToTable("approvals");
        builder.HasKey(a => a.ApprovalId);
        builder.Property(a => a.ApprovalId).ValueGeneratedNever();

        builder.Property(a => a.TenantId).IsRequired().HasMaxLength(128);
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(64);
        builder.Property(a => a.DecidedBy).IsRequired().HasMaxLength(128);
        builder.Property(a => a.Reason).IsRequired().HasMaxLength(1024);

        builder.HasIndex(a => new { a.TenantId, a.CaseId });
    }
}
