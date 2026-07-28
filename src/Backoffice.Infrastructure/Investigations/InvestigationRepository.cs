using Backoffice.Application.Investigations;
using Backoffice.Domain.Investigations;
using Backoffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Backoffice.Infrastructure.Investigations;

public sealed class InvestigationRepository(BackofficeDbContext dbContext) : IInvestigationRepository
{
    public Task<Investigation?> FindByIdAsync(string tenantId, Guid caseId, Guid investigationId, CancellationToken cancellationToken = default) =>
        dbContext.Investigations.FirstOrDefaultAsync(
            i => i.TenantId == tenantId && i.CaseId == caseId && i.InvestigationId == investigationId, cancellationToken);

    public void Add(Investigation investigation) => dbContext.Investigations.Add(investigation);
}
