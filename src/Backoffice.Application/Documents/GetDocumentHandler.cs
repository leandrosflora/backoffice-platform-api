using Backoffice.Application.Cases;

namespace Backoffice.Application.Documents;

public sealed class DocumentNotFoundException(Guid documentId) : Exception($"Document '{documentId}' was not found.")
{
    public Guid DocumentId { get; } = documentId;
}

public sealed class GetDocumentHandler(ICaseRepository caseRepository, IDocumentRepository documentRepository)
{
    public async Task<DocumentResponse> HandleAsync(
        string tenantId, Guid caseId, Guid documentId, CancellationToken cancellationToken = default)
    {
        // Confirm the case is visible to this tenant first, so an unknown/foreign case
        // yields the same 404 a foreign document would (no cross-tenant existence leakage).
        _ = await caseRepository.FindByIdAsync(tenantId, caseId, cancellationToken)
            ?? throw new CaseNotFoundException(caseId);

        var document = await documentRepository.FindByIdAsync(tenantId, caseId, documentId, cancellationToken)
            ?? throw new DocumentNotFoundException(documentId);

        return document.ToResponse();
    }
}

public sealed class ListEvidenceHandler(ICaseRepository caseRepository, IEvidenceRepository evidenceRepository)
{
    public async Task<IReadOnlyList<EvidenceResponse>> HandleAsync(
        string tenantId, Guid caseId, CancellationToken cancellationToken = default)
    {
        _ = await caseRepository.FindByIdAsync(tenantId, caseId, cancellationToken)
            ?? throw new CaseNotFoundException(caseId);

        var evidence = await evidenceRepository.ListByCaseAsync(tenantId, caseId, cancellationToken);
        return evidence.Select(e => e.ToResponse()).ToList();
    }
}
