namespace Backoffice.Domain.Approvals;

public sealed class Approval
{
    public Guid ApprovalId { get; private init; }
    public Guid CaseId { get; private init; }
    public string TenantId { get; private init; } = string.Empty;
    public Guid RecommendationId { get; private init; }
    public long RecommendationVersion { get; private init; }
    public ApprovalStatus Status { get; private init; }
    public string DecidedBy { get; private init; } = string.Empty;
    public string Reason { get; private init; } = string.Empty;
    public DateTimeOffset DecidedAt { get; private init; }

    /// <summary>How long this decision itself remains valid for gating execution (spec:
    /// governed-execution, "Execution requires a currently-valid approval") — distinct from
    /// the case's own approval-request deadline (see Case.ApprovalDeadline).</summary>
    public DateTimeOffset? ExpiresAt { get; private init; }

    private Approval() { }

    public static Approval Decide(
        Guid caseId,
        string tenantId,
        Guid recommendationId,
        long recommendationVersion,
        ApprovalStatus status,
        string decidedBy,
        string reason,
        DateTimeOffset now,
        TimeSpan validity) => new()
    {
        ApprovalId = Guid.NewGuid(),
        CaseId = caseId,
        TenantId = tenantId,
        RecommendationId = recommendationId,
        RecommendationVersion = recommendationVersion,
        Status = status,
        DecidedBy = decidedBy,
        Reason = reason,
        DecidedAt = now,
        ExpiresAt = status == ApprovalStatus.Approved ? now.Add(validity) : null,
    };
}
