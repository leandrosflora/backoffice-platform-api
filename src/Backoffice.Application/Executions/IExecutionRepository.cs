using Backoffice.Domain.Executions;

namespace Backoffice.Application.Executions;

public interface IExecutionRepository
{
    Task<Execution?> FindByIdAsync(string tenantId, Guid caseId, Guid executionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Execution>> ListByCaseAsync(string tenantId, Guid caseId, CancellationToken cancellationToken = default);

    void Add(Execution execution);
}

public interface IIdempotencyRecordRepository
{
    Task<IdempotencyRecord?> FindAsync(string tenantId, Guid caseId, string idempotencyKey, CancellationToken cancellationToken = default);

    void Add(IdempotencyRecord record);
}
