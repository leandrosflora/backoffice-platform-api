namespace Backoffice.Domain.Eventing;

/// <summary>
/// Immutable audit trail linking a replayed dead letter back to its original event, actor,
/// and reason (spec: eventing-reliability, "Human-authorized dead-letter replay").
/// </summary>
public sealed class ReplayAuditEntry
{
    public long Id { get; private init; }
    public long DeadLetterId { get; private init; }
    public Guid OriginalEventId { get; private init; }
    public Guid ReplayEventId { get; private init; }
    public string TenantId { get; private init; } = string.Empty;
    public string ActorId { get; private init; } = string.Empty;
    public string Reason { get; private init; } = string.Empty;
    public Guid CorrelationId { get; private init; }
    public DateTimeOffset CreatedAt { get; private init; }

    private ReplayAuditEntry() { }

    public static ReplayAuditEntry Create(
        long deadLetterId,
        Guid originalEventId,
        Guid replayEventId,
        string tenantId,
        string actorId,
        string reason,
        Guid correlationId,
        DateTimeOffset now) => new()
    {
        DeadLetterId = deadLetterId,
        OriginalEventId = originalEventId,
        ReplayEventId = replayEventId,
        TenantId = tenantId,
        ActorId = actorId,
        Reason = reason,
        CorrelationId = correlationId,
        CreatedAt = now,
    };
}
