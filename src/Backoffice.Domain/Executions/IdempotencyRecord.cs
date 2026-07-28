namespace Backoffice.Domain.Executions;

/// <summary>
/// Ledger entry enforcing BR-017/018/019: the same Idempotency-Key with the same command
/// hash replays the prior result; the same key with a different hash is a conflict.
/// </summary>
public sealed class IdempotencyRecord
{
    public Guid Id { get; private init; }
    public string TenantId { get; private init; } = string.Empty;
    public Guid CaseId { get; private init; }
    public string IdempotencyKey { get; private init; } = string.Empty;
    public string CommandHash { get; private init; } = string.Empty;
    public Guid ExecutionId { get; private init; }
    public DateTimeOffset CreatedAt { get; private init; }

    private IdempotencyRecord() { }

    public static IdempotencyRecord Create(
        string tenantId, Guid caseId, string idempotencyKey, string commandHash, Guid executionId, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        CaseId = caseId,
        IdempotencyKey = idempotencyKey,
        CommandHash = commandHash,
        ExecutionId = executionId,
        CreatedAt = now,
    };
}
