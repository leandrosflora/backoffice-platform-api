using Backoffice.Application.Cases;
using Backoffice.Domain.Cases;
using Microsoft.EntityFrameworkCore;

namespace Backoffice.Infrastructure.Persistence;

public sealed class CaseRepository(BackofficeDbContext dbContext) : ICaseRepository
{
    public Task<Case?> FindByExternalReferenceAsync(string tenantId, string externalReference, CancellationToken cancellationToken = default) =>
        dbContext.Cases
            .Include(c => c.Timeline)
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.ExternalReference == externalReference, cancellationToken);

    public Task<Case?> FindByIdAsync(string tenantId, Guid caseId, CancellationToken cancellationToken = default) =>
        dbContext.Cases
            .Include(c => c.Timeline)
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.CaseId == caseId, cancellationToken);

    public async Task<IReadOnlyList<Case>> ListAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        // Ordered client-side: SQLite (used by the test/dev provider) cannot translate
        // ORDER BY over DateTimeOffset, and result sets here are small enough that this
        // costs nothing in practice.
        var cases = await dbContext.Cases
            .Include(c => c.Timeline)
            .Where(c => c.TenantId == tenantId)
            .ToListAsync(cancellationToken);

        return cases.OrderByDescending(c => c.CreatedAt).ToList();
    }

    public void Add(Case @case) => dbContext.Cases.Add(@case);
}
