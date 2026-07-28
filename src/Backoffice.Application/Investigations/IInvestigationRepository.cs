using Backoffice.Domain.Investigations;

namespace Backoffice.Application.Investigations;

public interface IInvestigationRepository
{
    Task<Investigation?> FindByIdAsync(string tenantId, Guid caseId, Guid investigationId, CancellationToken cancellationToken = default);

    void Add(Investigation investigation);
}
