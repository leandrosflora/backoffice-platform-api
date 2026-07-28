using System.Text.Json.Serialization;

namespace Backoffice.Application.Policy;

/// <summary>
/// Wire format for policies/authorization.rego's actual `input.*` field access, which is
/// snake_case (e.g. `input.subject.tenant_id`, `input.context.case_version`) even though
/// contracts/schemas/policy-contracts.yaml documents the logical shape in camelCase. The
/// Rego file is reused unmodified (design.md), so this DTO matches what it really reads,
/// not the schema's casing.
/// </summary>
public sealed record PolicySubject(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("tenant_id")] string TenantId,
    [property: JsonPropertyName("roles")] IReadOnlyList<string> Roles,
    [property: JsonPropertyName("authentication_method")] string? AuthenticationMethod = null,
    [property: JsonPropertyName("token_id")] string? TokenId = null);

public sealed record PolicyResource(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("tenant_id")] string TenantId,
    [property: JsonPropertyName("state")] string? State = null,
    [property: JsonPropertyName("owner_id")] string? OwnerId = null);

public sealed record AuthorizationInput(
    [property: JsonPropertyName("subject")] PolicySubject Subject,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("resource")] PolicyResource Resource,
    [property: JsonPropertyName("purpose")] string Purpose,
    [property: JsonPropertyName("correlation_id")] string CorrelationId,
    [property: JsonPropertyName("context")] Dictionary<string, object?> Context);

public sealed record AuthorizationDecision(
    [property: JsonPropertyName("allow")] bool Allow,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("obligations")] IReadOnlyList<string> Obligations)
{
    /// <summary>
    /// Not part of OPA's response — set by the client wrapper when the PDP itself could not
    /// be reached/evaluated, so the enforcer can distinguish "PDP said no" (403) from
    /// "PDP unavailable" (503), both of which still deny (fail-closed).
    /// </summary>
    [JsonIgnore]
    public bool PdpAvailable { get; init; } = true;

    public static AuthorizationDecision Unavailable(string reason) => new(false, reason, [])
    {
        PdpAvailable = false,
    };
}

/// <summary>Canonical action names, matching contracts/schemas/policy-contracts.yaml $defs/AuthorizationInput/properties/action.</summary>
public static class PolicyActions
{
    public const string CaseCreate = "case.create";
    public const string CaseRead = "case.read";
    public const string CaseCancel = "case.cancel";
    public const string DocumentRegister = "document.register";
    public const string DocumentRead = "document.read";
    public const string EvidenceRead = "evidence.read";
    public const string InvestigationExecute = "investigation.execute";
    public const string RecommendationCreate = "recommendation.create";
    public const string ApprovalDecide = "approval.decide";
    public const string ExecutionRequest = "execution.request";
    public const string ExecutionRead = "execution.read";
    public const string ReconciliationResolve = "reconciliation.resolve";
    public const string AuditRead = "audit.read";
    public const string EventRead = "event.read";
    public const string EventReplay = "event.replay";
    public const string TimerSchedule = "timer.schedule";
}

public static class PolicyPurposes
{
    public const string CaseProcessing = "CASE_PROCESSING";
    public const string Approval = "APPROVAL";
    public const string Execution = "EXECUTION";
    public const string Audit = "AUDIT";
    public const string Operations = "OPERATIONS";
}

public static class PolicyResourceTypes
{
    public const string Case = "CASE";
    public const string Document = "DOCUMENT";
    public const string Evidence = "EVIDENCE";
    public const string Recommendation = "RECOMMENDATION";
    public const string Approval = "APPROVAL";
    public const string Execution = "EXECUTION";
    public const string Audit = "AUDIT";
    public const string Event = "EVENT";
    public const string Timer = "TIMER";
    public const string DeadLetter = "DEAD_LETTER";
}

public static class PolicySubjectTypes
{
    public const string Human = "HUMAN";
    public const string Workload = "WORKLOAD";
}
