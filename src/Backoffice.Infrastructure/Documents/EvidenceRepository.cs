using Backoffice.Application.Documents;
using Backoffice.Domain.Evidence;
using Backoffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Backoffice.Infrastructure.Documents;

public sealed class EvidenceRepository(BackofficeDbContext dbContext) : IEvidenceRepository
{
    public async Task<IReadOnlyList<EvidenceRecord>> ListByCaseAsync(string tenantId, Guid caseId, CancellationToken cancellationToken = default)
    {
        // Ordered client-side: SQLite (test/dev provider) cannot translate ORDER BY over
        // DateTimeOffset, and per-case evidence counts are small enough that this is fine.
        var evidence = await dbContext.Evidence
            .Where(e => e.TenantId == tenantId && e.CaseId == caseId)
            .ToListAsync(cancellationToken);

        return evidence.OrderBy(e => e.CreatedAt).ToList();
    }

    public void Add(EvidenceRecord evidence) => dbContext.Evidence.Add(evidence);
}
