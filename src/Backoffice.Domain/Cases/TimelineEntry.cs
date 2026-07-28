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

    /// <summary>
    /// `BR-###` rule id(s) that governed this transition, when it's a recommendation,
    /// approval, or execution decision (spec: audit-compliance, "Traceability to business
    /// rules"). Empty for transitions the traceability matrix doesn't cite (e.g. document
    /// intake, case cancellation).
    /// </summary>
    public IReadOnlyList<string> RuleReferences { get; private init; } = [];

    /// <summary>The OPA policy action (e.g. "recommendation.create") that authorized this
    /// transition, or null when the transition isn't itself a gated decision.</summary>
    public string? PolicyAction { get; private init; }

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
        DateTimeOffset occurredAt,
        IReadOnlyList<string>? ruleReferences = null,
        string? policyAction = null) => new()
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
        RuleReferences = ruleReferences ?? [],
        PolicyAction = policyAction,
    };
}
