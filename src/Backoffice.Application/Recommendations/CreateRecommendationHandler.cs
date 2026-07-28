using Backoffice.Application.Abstractions;
using Backoffice.Application.Cases;
using Backoffice.Application.Documents;
using Backoffice.Application.Investigations;
using Backoffice.Domain.Cases;
using Backoffice.Domain.Recommendations;

namespace Backoffice.Application.Recommendations;

public sealed class CreateRecommendationHandler(
    ICaseRepository caseRepository,
    IInvestigationRepository investigationRepository,
    IEvidenceRepository evidenceRepository,
    IRecommendationRepository recommendationRepository,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<RecommendationResponse> HandleAsync(
        string tenantId,
        Guid caseId,
        CreateRecommendationRequest request,
        string actorId,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        var @case = await caseRepository.FindByIdAsync(tenantId, caseId, cancellationToken)
            ?? throw new CaseNotFoundException(caseId);

        if (request.CaseVersion != @case.CaseVersion)
        {
            throw new CaseVersionConflictException(request.CaseVersion, @case.CaseVersion);
        }

        // AwaitingApproval is allowed too: a newer recommendation may supersede one still
        // pending human decision (spec: investigation-decision, "Recommendation versioning").
        if (@case.State is not (CaseState.UnderInvestigation or CaseState.DecisionProposed or CaseState.AwaitingApproval))
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

        var decision = RecommendationEngine.Decide(investigation.Findings, evidenceIds);
        var nextVersion = (@case.RecommendationVersion ?? 0) + 1;

        var recommendation = Recommendation.Create(
            caseId, tenantId, @case.CaseVersion, nextVersion, decision.Outcome, decision.Confidence,
            decision.Rationale, evidenceIds, decision.RuleReferences, actorId, clock.UtcNow);
        recommendationRepository.Add(recommendation);

        @case.RecordRecommendation(nextVersion, actorId);

        if (@case.State == CaseState.UnderInvestigation)
        {
            @case.Transition(
                @case.CaseVersion, CaseState.DecisionProposed, "DecisionProposed", actorId, "recommendation",
                correlationId, null, "Recommendation created.", clock.UtcNow);
        }

        // Only advance to AwaitingApproval; if the case is already there (a superseding
        // recommendation), leave it — AwaitingApproval has no self-transition in CaseLifecycle.
        if (@case.State == CaseState.DecisionProposed && decision.Outcome != RecommendationOutcome.Abstain)
        {
            @case.Transition(
                @case.CaseVersion, CaseState.AwaitingApproval, "ApprovalRequested", actorId, "recommendation",
                correlationId, null, "Actionable recommendation awaiting human approval.", clock.UtcNow);
            @case.SetApprovalDeadline(clock.UtcNow.Add(Case.ApprovalWindow));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return recommendation.ToResponse();
    }
}
