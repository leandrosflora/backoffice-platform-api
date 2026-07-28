using Backoffice.Domain.Cases;

namespace Backoffice.Application.Cases;

public interface ICaseRepository
{
    Task<Case?> FindByExternalReferenceAsync(string tenantId, string externalReference, CancellationToken cancellationToken = default);

    /// <summary>Loads a case only if it belongs to the given tenant; returns null otherwise (never leaks cross-tenant existence).</summary>
    Task<Case?> FindByIdAsync(string tenantId, Guid caseId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Case>> ListAsync(string tenantId, CancellationToken cancellationToken = default);

    void Add(Case @case);
}
