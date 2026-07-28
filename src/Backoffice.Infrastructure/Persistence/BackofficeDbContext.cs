using Backoffice.Domain.Approvals;
using Backoffice.Domain.Cases;
using Backoffice.Domain.Documents;
using Backoffice.Domain.Evidence;
using Backoffice.Domain.Executions;
using Backoffice.Domain.Investigations;
using Backoffice.Domain.Recommendations;
using Microsoft.EntityFrameworkCore;

namespace Backoffice.Infrastructure.Persistence;

public sealed class BackofficeDbContext(DbContextOptions<BackofficeDbContext> options) : DbContext(options)
{
    public DbSet<Case> Cases => Set<Case>();
    public DbSet<TimelineEntry> TimelineEntries => Set<TimelineEntry>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<EvidenceRecord> Evidence => Set<EvidenceRecord>();
    public DbSet<Investigation> Investigations => Set<Investigation>();
    public DbSet<Recommendation> Recommendations => Set<Recommendation>();
    public DbSet<Approval> Approvals => Set<Approval>();
    public DbSet<Execution> Executions => Set<Execution>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BackofficeDbContext).Assembly);
    }
}
