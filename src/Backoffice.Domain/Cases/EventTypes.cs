namespace Backoffice.Domain.Cases;

/// <summary>
/// Wire-format event type identifiers, matching `contracts/asyncapi/platform-events.yaml`'s
/// channel `address` values (the dotted, versioned form actually put on the wire — the
/// sibling `contracts/schemas/event-envelope.yaml`'s JSON-Schema `const` values omit the
/// `.v1` suffix, an inconsistency in the upstream spec itself). Used for both
/// <see cref="TimelineEntry"/>/outbox `EventType` and the resulting Kafka envelope's
/// `eventType` field, so a case's timeline and its published events always agree
/// (spec: platform-deployment, task 13.2 contract conformance).
/// </summary>
public static class EventTypes
{
    public const string CaseCreated = "backoffice.case.created.v1";
    public const string DocumentReceived = "backoffice.document.received.v1";
    public const string DocumentValidated = "backoffice.document.validated.v1";
    public const string EvidenceMissing = "backoffice.evidence.missing.v1";
    public const string DecisionProposed = "backoffice.decision.proposed.v1";
    public const string ApprovalRequested = "backoffice.approval.requested.v1";
    public const string DecisionApproved = "backoffice.decision.approved.v1";
    public const string DecisionRejected = "backoffice.decision.rejected.v1";
    public const string ExecutionRequested = "backoffice.execution.requested.v1";
    public const string ExecutionCompleted = "backoffice.execution.completed.v1";
    public const string ExecutionFailed = "backoffice.execution.failed.v1";
    public const string ReconciliationRequired = "backoffice.reconciliation.required.v1";
}
