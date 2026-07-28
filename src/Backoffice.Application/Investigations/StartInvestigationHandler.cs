using Backoffice.Application.Abstractions;
using Backoffice.Application.Cases;
using Backoffice.Application.Documents;
using Backoffice.Application.Policy;
using Backoffice.Domain.Cases;
using Backoffice.Domain.Investigations;

namespace Backoffice.Application.Investigations;

public sealed class StartInvestigationHandler(
    ICaseRepository caseRepository,
    IEvidenceRepository evidenceRepository,
    IInvestigationRepository investigationRepository,
    IUnitOfWork unitOfWork,
    IClock clock,
    PolicyEnforcer policyEnforcer)
{
    public async Task<InvestigationResponse> HandleAsync(
        string tenantId,
        Guid caseId,
        long expectedVersion,
        StartInvestigationRequest request,
        string actorId,
        IReadOnlyList<string> roles,
        string subjectType,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        var @case = await caseRepository.FindByIdAsync(tenantId, caseId, cancellationToken)
            ?? throw new CaseNotFoundException(caseId);

        if (expectedVersion != @case.CaseVersion)
        {
            throw new CaseVersionConflictException(expectedVersion, @case.CaseVersion);
        }

        var evidence = await evidenceRepository.ListByCaseAsync(tenantId, caseId, cancellationToken);
        var evidenceIds = evidence.Select(e => e.EvidenceId).ToList();

        // policies/authorization.rego's investigation.execute rule requires resource.state ==
        // DOCUMENTS_VALIDATED and evidence_present — stricter than this handler's own prior
        // interim allowance (retrying while already UNDER_INVESTIGATION, or with no evidence
        // at all). OPA is authoritative and unmodified, so both are now enforced exactly as
        // the policy defines them (spec: policy-authorization, "Every gated action...").
        await policyEnforcer.EnforceAsync(new AuthorizationInput(
            new PolicySubject(actorId, subjectType, tenantId, roles),
            PolicyActions.InvestigationExecute,
            new PolicyResource(PolicyResourceTypes.Case, caseId.ToString(), tenantId, @case.State.ToWireString()),
            PolicyPurposes.CaseProcessing,
            correlationId.ToString(),
            new Dictionary<string, object?>
            {
                ["case_version"] = expectedVersion,
                ["evidence_references"] = evidenceIds.Select(id => id.ToString()).ToList(),
            }),
            new Dictionary<string, bool>
            {
                ["verify-case-version"] = true,
                ["verify-evidence"] = evidenceIds.Count > 0,
            },
            cancellationToken);

        @case.Transition(
            @case.CaseVersion, CaseState.UnderInvestigation, "InvestigationStarted", actorId, "investigation",
            correlationId, null, "Investigation started.", clock.UtcNow);

        var findings = InvestigationEngine.Run(evidenceIds);
        var investigation = Investigation.Complete(caseId, tenantId, findings, clock.UtcNow);
        investigationRepository.Add(investigation);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return investigation.ToResponse();
    }
}
