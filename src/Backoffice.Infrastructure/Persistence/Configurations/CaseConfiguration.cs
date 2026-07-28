using Backoffice.Domain.Cases;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backoffice.Infrastructure.Persistence.Configurations;

public sealed class CaseConfiguration : IEntityTypeConfiguration<Case>
{
    public void Configure(EntityTypeBuilder<Case> builder)
    {
        builder.ToTable("cases");
        builder.HasKey(c => c.CaseId);
        // CaseId is always assigned client-side (Case.Create), never store-generated;
        // without this, EF's "non-default Guid key => must already exist" heuristic can
        // misclassify a freshly created aggregate as Unchanged/Modified instead of Added.
        builder.Property(c => c.CaseId).ValueGeneratedNever();

        builder.Property(c => c.TenantId).IsRequired().HasMaxLength(128);
        builder.Property(c => c.ExternalReference).IsRequired().HasMaxLength(256);
        builder.Property(c => c.DisputeType).HasConversion<string>().HasMaxLength(64);
        builder.Property(c => c.Channel).HasConversion<string>().HasMaxLength(64);
        builder.Property(c => c.State).HasConversion<string>().HasMaxLength(64);
        builder.Property(c => c.Priority).HasConversion<string>().HasMaxLength(64);
        builder.Property(c => c.CaseVersion).IsConcurrencyToken(false);

        builder.OwnsOne(c => c.DisputedAmount, money =>
        {
            money.Property(m => m.Currency).HasColumnName("disputed_amount_currency").HasMaxLength(3);
            money.Property(m => m.Amount).HasColumnName("disputed_amount_value").HasColumnType("decimal(18,2)");
        });

        builder.HasIndex(c => new { c.TenantId, c.ExternalReference }).IsUnique();

        builder.HasMany(c => c.Timeline)
            .WithOne()
            .HasForeignKey(t => t.CaseId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
