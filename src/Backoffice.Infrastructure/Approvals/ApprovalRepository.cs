using Backoffice.Application.Approvals;
using Backoffice.Domain.Approvals;
using Backoffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Backoffice.Infrastructure.Approvals;

public sealed class ApprovalRepository(BackofficeDbContext dbContext) : IApprovalRepository
{
    public Task<Approval?> FindByIdAsync(string tenantId, Guid caseId, Guid approvalId, CancellationToken cancellationToken = default) =>
        dbContext.Approvals.FirstOrDefaultAsync(
            a => a.TenantId == tenantId && a.CaseId == caseId && a.ApprovalId == approvalId, cancellationToken);

    public async Task<IReadOnlyList<Approval>> ListByCaseAsync(
        string tenantId,
        Guid caseId,
        CancellationToken cancellationToken = default)
    {
        // DateTimeOffset ordering is performed client-side because SQLite, used by API
        // integration tests and local development, cannot translate this ORDER BY.
        var approvals = await dbContext.Approvals
            .Where(a => a.TenantId == tenantId && a.CaseId == caseId)
            .ToListAsync(cancellationToken);

        return approvals.OrderBy(a => a.DecidedAt).ToList();
    }

    public void Add(Approval approval) => dbContext.Approvals.Add(approval);
}
