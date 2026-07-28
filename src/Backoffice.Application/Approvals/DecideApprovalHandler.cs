using Backoffice.Application.Abstractions;
using Backoffice.Application.Cases;
using Backoffice.Application.Policy;
using Backoffice.Domain.Approvals;
using Backoffice.Domain.Cases;

namespace Backoffice.Application.Approvals;

public sealed class DecideApprovalHandler(
    ICaseRepository caseRepository,
    IApprovalRepository approvalRepository,
    IUnitOfWork unitOfWork,
    IClock clock,
    PolicyEnforcer policyEnforcer)
{
    public async Task<ApprovalResponse> HandleAsync(
        string tenantId,
        Guid caseId,
        DecideApprovalRequest request,
        string actorId,
        IReadOnlyList<string> roles,
        string subjectType,
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

        // Kept as a specific, informative 409 rather than folding into the OPA gate below:
        // it is a data-consistency concern (the caller's view of the recommendation is
        // stale), not an authorization concern.
        if (request.RecommendationVersion != @case.RecommendationVersion)
        {
            throw new StaleRecommendationException(request.RecommendationVersion, @case.RecommendationVersion ?? 0);
        }

        // policies/authorization.rego's approval.decide rule encodes self-approval
        // prohibition and the alçada (authority-limit) check itself, uniformly for every
        // decision value — replacing this handler's prior interim SelfApprovalException/
        // AuthorityLimitExceededException checks (spec: policy-authorization).
        var selfApprovalOk = !string.Equals(actorId, @case.RecommendationActorId, StringComparison.Ordinal);
        await policyEnforcer.EnforceAsync(new AuthorizationInput(
            new PolicySubject(actorId, subjectType, tenantId, roles),
            PolicyActions.ApprovalDecide,
            new PolicyResource(PolicyResourceTypes.Approval, caseId.ToString(), tenantId, @case.State.ToWireString()),
            PolicyPurposes.Approval,
            correlationId.ToString(),
            new Dictionary<string, object?>
            {
                ["case_version"] = @case.CaseVersion,
                ["recommendation_actor_id"] = @case.RecommendationActorId,
                ["recommendation_version"] = request.RecommendationVersion,
                ["approved_recommendation_version"] = @case.RecommendationVersion,
                ["amount"] = @case.DisputedAmount.Amount,
                ["authority_limit"] = authorityLimit,
            }),
            new Dictionary<string, bool>
            {
                ["verify-case-version"] = true,
                ["verify-segregation-of-duties"] = selfApprovalOk,
            },
            cancellationToken);

        var status = request.Decision.ToStatus();

        var approval = Approval.Decide(
            caseId, tenantId, request.RecommendationId, request.RecommendationVersion, status,
            actorId, request.Reason, clock.UtcNow, Case.ApprovalWindow);
        approvalRepository.Add(approval);

        var (toState, eventType, ruleReferences) = status switch
        {
            ApprovalStatus.Approved => (CaseState.Approved, "DecisionApproved", BusinessRuleReferences.DecisionApproved),
            ApprovalStatus.Rejected => (CaseState.Rejected, "DecisionRejected", BusinessRuleReferences.DecisionRejected),
            // Not a documented event in contracts/asyncapi/platform-events.yaml, so no BR-### to cite.
            ApprovalStatus.MoreEvidenceRequired => (CaseState.MoreEvidenceRequired, "MoreEvidenceRequired", (IReadOnlyList<string>)[]),
            _ => throw new ArgumentOutOfRangeException(),
        };

        @case.Transition(
            @case.CaseVersion, toState, eventType, actorId, "approval", correlationId, null, request.Reason, clock.UtcNow,
            ruleReferences, PolicyActions.ApprovalDecide);
        @case.ClearApprovalDeadline();

        if (status == ApprovalStatus.Approved)
        {
            @case.RecordApproval(request.RecommendationVersion);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return approval.ToResponse();
    }
}
