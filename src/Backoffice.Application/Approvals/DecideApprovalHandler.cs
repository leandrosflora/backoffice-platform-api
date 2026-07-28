using Backoffice.Application.Abstractions;
using Backoffice.Application.Cases;
using Backoffice.Domain.Approvals;
using Backoffice.Domain.Cases;

namespace Backoffice.Application.Approvals;

public sealed class DecideApprovalHandler(
    ICaseRepository caseRepository,
    IApprovalRepository approvalRepository,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<ApprovalResponse> HandleAsync(
        string tenantId,
        Guid caseId,
        DecideApprovalRequest request,
        string actorId,
        decimal authorityLimit,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        var @case = await caseRepository.FindByIdAsync(tenantId, caseId, cancellationToken)
            ?? throw new CaseNotFoundException(caseId);

        if (request.CaseVersion != @case.CaseVersion)
        {
            throw new CaseVersionConflictException(request.CaseVersion, @case.CaseVersion);
        }

        // Lazy/on-read expiry: stands in for a dedicated background worker until section 8.
        if (@case.ExpireApprovalIfDue(clock.UtcNow, correlationId))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            throw new CaseNotAwaitingApprovalException(caseId);
        }

        if (@case.State != CaseState.AwaitingApproval)
        {
            throw new CaseNotAwaitingApprovalException(caseId);
        }

        if (request.RecommendationVersion != @case.RecommendationVersion)
        {
            throw new StaleRecommendationException(request.RecommendationVersion, @case.RecommendationVersion ?? 0);
        }

        // Segregation of duties (spec: human-approval, "Self-approval prohibition").
        if (string.Equals(actorId, @case.RecommendationActorId, StringComparison.Ordinal))
        {
            throw new SelfApprovalException(actorId);
        }

        var status = request.Decision.ToStatus();

        // Alçada: only an APPROVE decision commits to the disputed amount.
        if (status == ApprovalStatus.Approved && authorityLimit < @case.DisputedAmount.Amount)
        {
            throw new AuthorityLimitExceededException(authorityLimit, @case.DisputedAmount.Amount);
        }

        var approval = Approval.Decide(
            caseId, tenantId, request.RecommendationId, request.RecommendationVersion, status,
            actorId, request.Reason, clock.UtcNow, Case.ApprovalWindow);
        approvalRepository.Add(approval);

        var (toState, eventType) = status switch
        {
            ApprovalStatus.Approved => (CaseState.Approved, "DecisionApproved"),
            ApprovalStatus.Rejected => (CaseState.Rejected, "DecisionRejected"),
            ApprovalStatus.MoreEvidenceRequired => (CaseState.MoreEvidenceRequired, "MoreEvidenceRequired"),
            _ => throw new ArgumentOutOfRangeException(),
        };

        @case.Transition(@case.CaseVersion, toState, eventType, actorId, "approval", correlationId, null, request.Reason, clock.UtcNow);
        @case.ClearApprovalDeadline();

        if (status == ApprovalStatus.Approved)
        {
            @case.RecordApproval(request.RecommendationVersion);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return approval.ToResponse();
    }
}
