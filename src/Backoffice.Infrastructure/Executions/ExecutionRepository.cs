using Backoffice.Application.Executions;
using Backoffice.Domain.Executions;
using Backoffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Backoffice.Infrastructure.Executions;

public sealed class ExecutionRepository(BackofficeDbContext dbContext) : IExecutionRepository
{
    public Task<Execution?> FindByIdAsync(string tenantId, Guid caseId, Guid executionId, CancellationToken cancellationToken = default) =>
        dbContext.Executions.FirstOrDefaultAsync(
            e => e.TenantId == tenantId && e.CaseId == caseId && e.ExecutionId == executionId, cancellationToken);

    public async Task<IReadOnlyList<Execution>> ListByCaseAsync(string tenantId, Guid caseId, CancellationToken cancellationToken = default)
    {
        // Ordered client-side: SQLite (test/dev provider) cannot translate ORDER BY over
        // DateTimeOffset, and per-case execution counts are small enough that this is fine.
        var executions = await dbContext.Executions
            .Where(e => e.TenantId == tenantId && e.CaseId == caseId)
            .ToListAsync(cancellationToken);

        return executions.OrderBy(e => e.CreatedAt).ToList();
    }

    public void Add(Execution execution) => dbContext.Executions.Add(execution);
}

public sealed class IdempotencyRecordRepository(BackofficeDbContext dbContext) : IIdempotencyRecordRepository
{
    public Task<IdempotencyRecord?> FindAsync(string tenantId, Guid caseId, string idempotencyKey, CancellationToken cancellationToken = default) =>
        dbContext.IdempotencyRecords.FirstOrDefaultAsync(
            r => r.TenantId == tenantId && r.CaseId == caseId && r.IdempotencyKey == idempotencyKey, cancellationToken);

    public void Add(IdempotencyRecord record) => dbContext.IdempotencyRecords.Add(record);
}
