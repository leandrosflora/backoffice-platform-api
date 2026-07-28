using Backoffice.Domain.Audit;

namespace Backoffice.Application.Audit;

/// <summary>
/// Deliberately exposes only <see cref="Add"/> and a read — no update, delete, or
/// find-for-mutation method exists anywhere on this interface, so there is no code path
/// through which an audit record could be altered (spec: audit-compliance, "Audit records
/// cannot be modified").
/// </summary>
public interface IAuditRepository
{
    void Add(AuditRecord record);

    Task<IReadOnlyList<AuditRecord>> ListByTenantAsync(string tenantId, int limit, CancellationToken cancellationToken = default);
}
