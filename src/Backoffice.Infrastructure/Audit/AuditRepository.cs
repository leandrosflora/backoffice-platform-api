using Backoffice.Application.Audit;
using Backoffice.Domain.Audit;
using Backoffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Backoffice.Infrastructure.Audit;

public sealed class AuditRepository(BackofficeDbContext dbContext) : IAuditRepository
{
    public void Add(AuditRecord record) => dbContext.AuditRecords.Add(record);

    public async Task<IReadOnlyList<AuditRecord>> ListByTenantAsync(string tenantId, int limit, CancellationToken cancellationToken = default)
    {
        var records = await dbContext.AuditRecords
            .Where(r => r.TenantId == tenantId)
            .ToListAsync(cancellationToken);

        return records.OrderByDescending(r => r.Id).Take(limit).ToList();
    }
}
