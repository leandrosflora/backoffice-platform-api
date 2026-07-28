using Backoffice.Domain.Approvals;

namespace Backoffice.Application.Approvals;

public interface IApprovalRepository
{
    Task<Approval?> FindByIdAsync(string tenantId, Guid caseId, Guid approvalId, CancellationToken cancellationToken = default);

    void Add(Approval approval);
}
