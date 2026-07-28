using Backoffice.Application.Abstractions;
using Backoffice.Application.Cases;
using Backoffice.Application.Documents;
using Backoffice.Application.Investigations;
using Backoffice.Application.Policy;
using Backoffice.Domain.Cases;
using Backoffice.Domain.Recommendations;

namespace Backoffice.Application.Recommendations;

public sealed class CreateRecommendationHandler(
    ICaseRepository caseRepository,
    IInvestigationRepository investigationRepository,
    IEvidenceRepository evidenceRepository,
    IRecommendationRepository recommendationRepository,
    IUnitOfWork unitOfWork,
    IClock clock,
    PolicyEnforcer policyEnforcer)
{
    public async Task<RecommendationResponse> HandleAsync(
        string tenantId,
        Guid caseId,
        CreateRecommendationRequest request,
        string actorId,
        IReadOnlyList<string> roles,
        string subjectType,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        var @case = await caseRepository.FindByIdAsync(tenantId, caseId, cancellationToken)
            ?? throw new CaseNotFoundException(caseId);

        if (request.CaseVersion != @case.CaseVersion)
        {
            throw new CaseVersionConflictException(request.CaseVersion, @case.CaseVersion);
        }

        // policies/authorization.rego's recommendation.create rule requires resource.state ==
        // UNDER_INVESTIGATION exactly — narrower than this handler's prior interim allowance
        // (also permitting a superseding recommendation while AWAITING_APPROVAL). OPA is
        // authoritative and unmodified, so only the UNDER_INVESTIGATION entry point remains;
        // reaching UNDER_INVESTIGATION again after MORE_EVIDENCE_REQUIRED is a re-investigation
        // concern OPA's rule set does not yet define, so that loop is out of scope here.
        if (@case.State != CaseState.UnderInvestigation)
        {
            throw new InvalidCaseTransitionException(@case.State, CaseState.DecisionProposed);
        }

        var investigation = await investigationRepository.FindByIdAsync(tenantId, caseId, request.InvestigationId, cancellationToken)
            ?? throw new InvestigationNotFoundException(request.InvestigationId);

        var evidence = await evidenceRepository.ListByCaseAsync(tenantId, caseId, cancellationToken);
        var evidenceIds = evidence.Select(e => e.EvidenceId).ToList();
        if (evidenceIds.Count == 0)
        {
            throw new NoGroundingEvidenceException(caseId);
        }

        await policyEnforcer.EnforceAsync(new AuthorizationInput(
            new PolicySubject(actorId, subjectType, tenantId, roles),
            PolicyActions.RecommendationCreate,
            new PolicyResource(PolicyResourceTypes.Recommendation, caseId.ToString(), tenantId, @case.State.ToWireString()),
            PolicyPurposes.CaseProcessing,
            correlationId.ToString(),
            new Dictionary<string, object?>
            {
                ["case_version"] = @case.CaseVersion,
                ["evidence_references"] = evidenceIds.Select(id => id.ToString()).ToList(),
            }),
            new Dictionary<string, bool> { ["verify-case-version"] = true, ["verify-evidence"] = true },
            cancellationToken);

        var decision = RecommendationEngine.Decide(investigation.Findings, evidenceIds);
        var nextVersion = (@case.RecommendationVersion ?? 0) + 1;

        var recommendation = Recommendation.Create(
            caseId, tenantId, @case.CaseVersion, nextVersion, decision.Outcome, decision.Confidence,
            decision.Rationale, evidenceIds, decision.RuleReferences, actorId, clock.UtcNow);
        recommendationRepository.Add(recommendation);

        @case.RecordRecommendation(nextVersion, actorId);

        @case.Transition(
            @case.CaseVersion, CaseState.DecisionProposed, EventTypes.DecisionProposed, actorId, "recommendation",
            correlationId, null, "Recommendation created.", clock.UtcNow,
            decision.RuleReferences, PolicyActions.RecommendationCreate);

        if (decision.Outcome != RecommendationOutcome.Abstain)
        {
            @case.Transition(
                @case.CaseVersion, CaseState.AwaitingApproval, EventTypes.ApprovalRequested, actorId, "recommendation",
                correlationId, null, "Actionable recommendation awaiting human approval.", clock.UtcNow,
                BusinessRuleReferences.ApprovalRequested, PolicyActions.RecommendationCreate);
            @case.SetApprovalDeadline(clock.UtcNow.Add(Case.ApprovalWindow));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return recommendation.ToResponse();
    }
}
