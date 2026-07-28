namespace Backoffice.Domain.Audit;

/// <summary>
/// Append-only ingestion of a published domain event — the Audit Service's system of record
/// for compliance review (spec: audit-compliance, "Append-only audit ingestion"). Exposes no
/// mutation method whatsoever, by design: once created there is no way to alter or remove a
/// record, which is how "no update or delete path exposed" is actually enforced here rather
/// than merely documented.
/// </summary>
public sealed class AuditRecord
{
    public long Id { get; private init; }
    public Guid EventId { get; private init; }
    public string EventType { get; private init; } = string.Empty;
    public string TenantId { get; private init; } = string.Empty;
    public Guid AggregateId { get; private init; }
    public Guid CorrelationId { get; private init; }
    public Guid? CausationId { get; private init; }

    /// <summary>The OPA policy action that governed the decision, when the event is a
    /// recommendation/approval/execution decision (spec: audit-compliance, "Traceability to
    /// business rules").</summary>
    public string? PolicyAction { get; private init; }

    public IReadOnlyList<string> RuleReferences { get; private init; } = [];
    public string PayloadJson { get; private init; } = "{}";
    public DateTimeOffset OccurredAt { get; private init; }
    public DateTimeOffset IngestedAt { get; private init; }

    private AuditRecord() { }

    public static AuditRecord Create(
        Guid eventId,
        string eventType,
        string tenantId,
        Guid aggregateId,
        Guid correlationId,
        Guid? causationId,
        string? policyAction,
        IReadOnlyList<string> ruleReferences,
        string payloadJson,
        DateTimeOffset occurredAt,
        DateTimeOffset ingestedAt) => new()
    {
        EventId = eventId,
        EventType = eventType,
        TenantId = tenantId,
        AggregateId = aggregateId,
        CorrelationId = correlationId,
        CausationId = causationId,
        PolicyAction = policyAction,
        RuleReferences = ruleReferences,
        PayloadJson = payloadJson,
        OccurredAt = occurredAt,
        IngestedAt = ingestedAt,
    };
}
