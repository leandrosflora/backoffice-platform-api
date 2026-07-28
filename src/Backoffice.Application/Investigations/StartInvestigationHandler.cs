using Backoffice.Application.Abstractions;
using Backoffice.Application.Cases;
using Backoffice.Application.Documents;
using Backoffice.Domain.Cases;
using Backoffice.Domain.Investigations;

namespace Backoffice.Application.Investigations;

public sealed class StartInvestigationHandler(
    ICaseRepository caseRepository,
    IEvidenceRepository evidenceRepository,
    IInvestigationRepository investigationRepository,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<InvestigationResponse> HandleAsync(
        string tenantId,
        Guid caseId,
        long expectedVersion,
        StartInvestigationRequest request,
        string actorId,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        var @case = await caseRepository.FindByIdAsync(tenantId, caseId, cancellationToken)
            ?? throw new CaseNotFoundException(caseId);

        if (expectedVersion != @case.CaseVersion)
        {
            throw new CaseVersionConflictException(expectedVersion, @case.CaseVersion);
        }

        // Idempotent per contract (x-idempotent: true on investigation.execute): a case
        // already under investigation can start another investigation pass without a
        // further state transition; any other state is an invalid transition attempt.
        if (@case.State == CaseState.DocumentsValidated)
        {
            @case.Transition(
                @case.CaseVersion, CaseState.UnderInvestigation, "InvestigationStarted", actorId, "investigation",
                correlationId, null, "Investigation started.", clock.UtcNow);
        }
        else if (@case.State != CaseState.UnderInvestigation)
        {
            throw new InvalidCaseTransitionException(@case.State, CaseState.UnderInvestigation);
        }

        var evidence = await evidenceRepository.ListByCaseAsync(tenantId, caseId, cancellationToken);
        var evidenceIds = evidence.Select(e => e.EvidenceId).ToList();

        var findings = InvestigationEngine.Run(evidenceIds);
        var investigation = Investigation.Complete(caseId, tenantId, findings, clock.UtcNow);
        investigationRepository.Add(investigation);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return investigation.ToResponse();
    }
}
