namespace Backoffice.Domain.Cases;

/// <summary>
/// Append-only audit record of a command/transition applied to a case.
/// Never updated or deleted once written.
/// </summary>
public sealed class TimelineEntry
{
    public Guid Id { get; private init; }
    public Guid CaseId { get; private init; }
    public long CaseVersion { get; private init; }
    public string EventType { get; private init; } = string.Empty;
    public string ActorId { get; private init; } = string.Empty;
    public string Origin { get; private init; } = string.Empty;
    public Guid CorrelationId { get; private init; }
    public Guid? CausationId { get; private init; }
    public string Reason { get; private init; } = string.Empty;
    public DateTimeOffset OccurredAt { get; private init; }

    private TimelineEntry() { }

    public static TimelineEntry Create(
        Guid caseId,
        long caseVersion,
        string eventType,
        string actorId,
        string origin,
        Guid correlationId,
        Guid? causationId,
        string reason,
        DateTimeOffset occurredAt) => new()
    {
        Id = Guid.NewGuid(),
        CaseId = caseId,
        CaseVersion = caseVersion,
        EventType = eventType,
        ActorId = actorId,
        Origin = origin,
        CorrelationId = correlationId,
        CausationId = causationId,
        Reason = reason,
        OccurredAt = occurredAt,
    };
}
