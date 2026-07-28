using Backoffice.Domain.Evidence;

namespace Backoffice.Application.Documents;

public interface IEvidenceRepository
{
    Task<IReadOnlyList<EvidenceRecord>> ListByCaseAsync(string tenantId, Guid caseId, CancellationToken cancellationToken = default);

    void Add(EvidenceRecord evidence);
}
