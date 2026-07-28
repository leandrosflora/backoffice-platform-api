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

    public void Add(Approval approval) => dbContext.Approvals.Add(approval);
}
