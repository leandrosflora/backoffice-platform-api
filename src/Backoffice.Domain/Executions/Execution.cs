namespace Backoffice.Domain.Executions;

public sealed class InvalidExecutionTransitionException(ExecutionStatus from, ExecutionStatus to)
    : Exception($"Execution transition from '{from}' to '{to}' is not allowed.")
{
}

/// <summary>
/// A governed, idempotent mutating operation against a system of record. Never retried
/// blindly on an ambiguous result — see MarkReconciliationRequired (spec: governed-execution).
/// </summary>
public sealed class Execution
{
    public Guid ExecutionId { get; private init; }
    public Guid CaseId { get; private init; }
    public string TenantId { get; private init; } = string.Empty;
    public ExecutionStatus Status { get; private set; }
    public string IdempotencyKey { get; private init; } = string.Empty;
    public string CommandHash { get; private init; } = string.Empty;
    public string? ExternalReference { get; private set; }
    public DateTimeOffset CreatedAt { get; private init; }
    public DateTimeOffset? CompletedAt { get; private set; }

    private Execution() { }

    public static Execution Create(
        Guid caseId, string tenantId, string idempotencyKey, string commandHash, DateTimeOffset now) => new()
    {
        ExecutionId = Guid.NewGuid(),
        CaseId = caseId,
        TenantId = tenantId,
        Status = ExecutionStatus.Pending,
        IdempotencyKey = idempotencyKey,
        CommandHash = commandHash,
        CreatedAt = now,
    };

    public void MarkSucceeded(string? externalReference, DateTimeOffset now)
    {
        RequireStatus(ExecutionStatus.Pending, ExecutionStatus.Succeeded);
        Status = ExecutionStatus.Succeeded;
        ExternalReference = externalReference;
        CompletedAt = now;
    }

    public void MarkFailed(DateTimeOffset now)
    {
        RequireStatus(ExecutionStatus.Pending, ExecutionStatus.Failed);
        Status = ExecutionStatus.Failed;
        CompletedAt = now;
    }

    /// <summary>An indeterminate result (e.g. timeout) never becomes a silent success or a
    /// blind retry — it always requires explicit reconciliation.</summary>
    public void MarkReconciliationRequired(DateTimeOffset now)
    {
        RequireStatus(ExecutionStatus.Pending, ExecutionStatus.ReconciliationRequired);
        Status = ExecutionStatus.ReconciliationRequired;
        CompletedAt = now;
    }

    /// <summary>Resolves a pending reconciliation. ESCALATED leaves status unchanged
    /// (still RECONCILIATION_REQUIRED) since it defers the decision rather than closing it.</summary>
    public void Reconcile(ReconciliationResolution resolution, DateTimeOffset now)
    {
        if (Status != ExecutionStatus.ReconciliationRequired)
        {
            throw new InvalidExecutionTransitionException(Status, ExecutionStatus.Reconciled);
        }

        switch (resolution)
        {
            case ReconciliationResolution.ConfirmedSucceeded:
                Status = ExecutionStatus.Reconciled;
                CompletedAt = now;
                break;
            case ReconciliationResolution.ConfirmedFailed:
                Status = ExecutionStatus.Failed;
                CompletedAt = now;
                break;
            case ReconciliationResolution.Escalated:
                break;
        }
    }

    private void RequireStatus(ExecutionStatus expected, ExecutionStatus target)
    {
        if (Status != expected)
        {
            throw new InvalidExecutionTransitionException(Status, target);
        }
    }
}
