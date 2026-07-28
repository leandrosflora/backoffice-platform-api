using System.Text.Json;
using Backoffice.Domain.Approvals;
using Backoffice.Domain.Audit;
using Backoffice.Domain.Cases;
using Backoffice.Domain.Documents;
using Backoffice.Domain.Evidence;
using Backoffice.Domain.Eventing;
using Backoffice.Domain.Executions;
using Backoffice.Domain.Investigations;
using Backoffice.Domain.Recommendations;
using Microsoft.EntityFrameworkCore;

namespace Backoffice.Infrastructure.Persistence;

public sealed class BackofficeDbContext(DbContextOptions<BackofficeDbContext> options) : DbContext(options)
{
    private const string EventsTopic = "backoffice.events.v1";
    private const string Producer = "intelligent-backoffice-dotnet";

    public DbSet<Case> Cases => Set<Case>();
    public DbSet<TimelineEntry> TimelineEntries => Set<TimelineEntry>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<EvidenceRecord> Evidence => Set<EvidenceRecord>();
    public DbSet<Investigation> Investigations => Set<Investigation>();
    public DbSet<Recommendation> Recommendations => Set<Recommendation>();
    public DbSet<Approval> Approvals => Set<Approval>();
    public DbSet<Execution> Executions => Set<Execution>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
    public DbSet<InboxRecord> Inbox => Set<InboxRecord>();
    public DbSet<EventTimer> Timers => Set<EventTimer>();
    public DbSet<DeadLetter> DeadLetters => Set<DeadLetter>();
    public DbSet<ReplayAuditEntry> ReplayAudits => Set<ReplayAuditEntry>();
    public DbSet<AuditRecord> AuditRecords => Set<AuditRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BackofficeDbContext).Assembly);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        AppendOutboxRowsForNewTimelineEntries();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        AppendOutboxRowsForNewTimelineEntries();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Application-level replacement for the Python sample's SQLite `timeline_to_outbox`
    /// trigger (design.md): every newly added <see cref="TimelineEntry"/> gets a matching
    /// outbox row in the same SaveChanges call, so the aggregate/timeline write and the
    /// published-event write can never observe one without the other (spec:
    /// eventing-reliability, "Aggregate save and outbox write are atomic"). The payload is a
    /// generic snapshot of the timeline entry's own fields — mapping each of the ~15
    /// documented event types to its precise contracts/schemas/event-envelope.yaml payload
    /// shape is deferred; the outbox/dispatch/retry/DLQ mechanics this section implements
    /// don't depend on that per-event fidelity.
    /// </summary>
    private void AppendOutboxRowsForNewTimelineEntries()
    {
        var newEntries = ChangeTracker.Entries<TimelineEntry>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity)
            .ToList();

        if (newEntries.Count == 0)
        {
            return;
        }

        var tenantIdsByCaseId = ChangeTracker.Entries<Case>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified)
            .Select(e => e.Entity)
            .ToDictionary(c => c.CaseId, c => c.TenantId);

        foreach (var entry in newEntries)
        {
            if (!tenantIdsByCaseId.TryGetValue(entry.CaseId, out var tenantId))
            {
                continue; // Defensive: every TimelineEntry is written alongside its owning Case.
            }

            var payloadJson = JsonSerializer.Serialize(new
            {
                caseId = entry.CaseId,
                caseVersion = entry.CaseVersion,
                eventType = entry.EventType,
                actorId = entry.ActorId,
                origin = entry.Origin,
                reason = entry.Reason,
                occurredAt = entry.OccurredAt,
                // Populated only for recommendation/approval/execution decisions (spec:
                // audit-compliance, "Traceability to business rules") — empty/null otherwise.
                ruleReferences = entry.RuleReferences,
                policyAction = entry.PolicyAction,
            });

            Outbox.Add(OutboxMessage.Create(
                entry.CaseId, tenantId, entry.EventType, EventsTopic, entry.CaseId.ToString(),
                entry.CorrelationId, entry.CausationId, Producer, payloadJson, entry.OccurredAt));
        }
    }
}
