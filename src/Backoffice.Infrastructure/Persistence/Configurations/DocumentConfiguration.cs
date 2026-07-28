using Backoffice.Domain.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backoffice.Infrastructure.Persistence.Configurations;

public sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    private const char ReasonSeparator = '\u001F';

    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("documents");
        builder.HasKey(d => d.DocumentId);
        builder.Property(d => d.DocumentId).ValueGeneratedNever();

        builder.Property(d => d.TenantId).IsRequired().HasMaxLength(128);
        builder.Property(d => d.DocumentType).HasConversion<string>().HasMaxLength(64);
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(64);
        builder.Property(d => d.MediaType).HasConversion<string>().HasMaxLength(64);
        builder.Property(d => d.Checksum).IsRequired().HasMaxLength(64);
        builder.Property(d => d.StorageReference).IsRequired().HasMaxLength(1024);

        builder.Property(d => d.RejectionReasons)
            .HasConversion(
                reasons => string.Join(ReasonSeparator, reasons),
                serialized => serialized.Length == 0
                    ? new List<string>()
                    : serialized.Split(ReasonSeparator, StringSplitOptions.None).ToList())
            .Metadata.SetValueComparer(new ValueComparer<List<string>>(
                (a, b) => a!.SequenceEqual(b!),
                a => a.Aggregate(0, (hash, s) => HashCode.Combine(hash, s.GetHashCode())),
                a => a.ToList()));

        builder.HasIndex(d => new { d.TenantId, d.CaseId });
    }
}
