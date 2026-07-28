using Backoffice.Domain.Approvals;

namespace Backoffice.Application.Approvals;

public enum ApprovalDecision
{
    Approve,
    Reject,
    RequestMoreEvidence,
}

public sealed record DecideApprovalRequest(
    long CaseVersion,
    Guid RecommendationId,
    long RecommendationVersion,
    ApprovalDecision Decision,
    string Reason,
    IReadOnlyList<Guid>? EvidenceReferences);

public sealed record ApprovalResponse(
    Guid ApprovalId,
    Guid CaseId,
    Guid RecommendationId,
    long RecommendationVersion,
    ApprovalStatus Status,
    string DecidedBy,
    string Reason,
    DateTimeOffset DecidedAt,
    DateTimeOffset? ExpiresAt);

public static class ApprovalMapping
{
    public static ApprovalStatus ToStatus(this ApprovalDecision decision) => decision switch
    {
        ApprovalDecision.Approve => ApprovalStatus.Approved,
        ApprovalDecision.Reject => ApprovalStatus.Rejected,
        ApprovalDecision.RequestMoreEvidence => ApprovalStatus.MoreEvidenceRequired,
        _ => throw new ArgumentOutOfRangeException(nameof(decision)),
    };

    public static ApprovalResponse ToResponse(this Approval approval) => new(
        approval.ApprovalId,
        approval.CaseId,
        approval.RecommendationId,
        approval.RecommendationVersion,
        approval.Status,
        approval.DecidedBy,
        approval.Reason,
        approval.DecidedAt,
        approval.ExpiresAt);
}
