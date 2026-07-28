namespace Backoffice.Application.Cases;

/// <summary>
/// `BR-###` rule ids per timeline event type, matching
/// contracts/asyncapi/platform-events.yaml's `x-business-rules` for the corresponding
/// documented event (spec: audit-compliance, "Traceability to business rules"). Only
/// recommendation/approval/execution decision transitions carry these — the rest are left
/// uncited, matching what the traceability matrix actually documents.
/// </summary>
public static class BusinessRuleReferences
{
    // DecisionProposedEvent's rule reference is decision-outcome-dependent (BR-008/BR-009),
    // already computed dynamically by RecommendationEngine — not listed here.
    public static readonly IReadOnlyList<string> ApprovalRequested = ["BR-011", "BR-013"];
    public static readonly IReadOnlyList<string> DecisionApproved = ["BR-012", "BR-013", "BR-014", "BR-015"];
    public static readonly IReadOnlyList<string> DecisionRejected = ["BR-012", "BR-014"];
    public static readonly IReadOnlyList<string> ExecutionRequested = ["BR-016", "BR-017", "BR-018", "BR-019"];
    public static readonly IReadOnlyList<string> ExecutionCompleted = ["BR-018", "BR-021"];
    public static readonly IReadOnlyList<string> ExecutionFailed = ["BR-020", "BR-021"];
    public static readonly IReadOnlyList<string> ReconciliationRequired = ["BR-020", "BR-021"];
}
