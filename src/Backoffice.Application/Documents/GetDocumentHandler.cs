using Backoffice.Application.Cases;
using Backoffice.Application.Policy;

namespace Backoffice.Application.Documents;

public sealed class DocumentNotFoundException(Guid documentId) : Exception($"Document '{documentId}' was not found.")
{
    public Guid DocumentId { get; } = documentId;
}

public sealed class GetDocumentHandler(ICaseRepository caseRepository, IDocumentRepository documentRepository, PolicyEnforcer policyEnforcer)
{
    public async Task<DocumentResponse> HandleAsync(
        string tenantId,
        Guid caseId,
        Guid documentId,
        string actorId,
        IReadOnlyList<string> roles,
        string subjectType,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        // Confirm the case is visible to this tenant first, so an unknown/foreign case
        // yields the same 404 a foreign document would (no cross-tenant existence leakage).
        var @case = await caseRepository.FindByIdAsync(tenantId, caseId, cancellationToken)
            ?? throw new CaseNotFoundException(caseId);

        var document = await documentRepository.FindByIdAsync(tenantId, caseId, documentId, cancellationToken)
            ?? throw new DocumentNotFoundException(documentId);

        await policyEnforcer.EnforceAsync(new AuthorizationInput(
            new PolicySubject(actorId, subjectType, tenantId, roles),
            PolicyActions.DocumentRead,
            new PolicyResource(PolicyResourceTypes.Document, documentId.ToString(), tenantId, @case.State.ToWireString()),
            PolicyPurposes.CaseProcessing,
            correlationId.ToString(),
            []), cancellationToken: cancellationToken);

        return document.ToResponse();
    }
}

public sealed class ListEvidenceHandler(ICaseRepository caseRepository, IEvidenceRepository evidenceRepository, PolicyEnforcer policyEnforcer)
{
    public async Task<IReadOnlyList<EvidenceResponse>> HandleAsync(
        string tenantId,
        Guid caseId,
        string actorId,
        IReadOnlyList<string> roles,
        string subjectType,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        var @case = await caseRepository.FindByIdAsync(tenantId, caseId, cancellationToken)
            ?? throw new CaseNotFoundException(caseId);

        await policyEnforcer.EnforceAsync(new AuthorizationInput(
            new PolicySubject(actorId, subjectType, tenantId, roles),
            PolicyActions.EvidenceRead,
            new PolicyResource(PolicyResourceTypes.Evidence, caseId.ToString(), tenantId, @case.State.ToWireString()),
            PolicyPurposes.CaseProcessing,
            correlationId.ToString(),
            []), cancellationToken: cancellationToken);

        var evidence = await evidenceRepository.ListByCaseAsync(tenantId, caseId, cancellationToken);
        return evidence.Select(e => e.ToResponse()).ToList();
    }
}
