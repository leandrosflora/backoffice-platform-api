using Backoffice.Domain.Documents;

namespace Backoffice.Application.Documents;

public interface IDocumentRepository
{
    Task<Document?> FindByIdAsync(string tenantId, Guid caseId, Guid documentId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Document>> ListByCaseAsync(string tenantId, Guid caseId, CancellationToken cancellationToken = default);

    void Add(Document document);
}
