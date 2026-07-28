namespace Backoffice.Application.Executions;

public sealed class ExecutionNotFoundException(Guid executionId) : Exception($"Execution '{executionId}' was not found.")
{
}

/// <summary>The case has no currently-valid APPROVED decision to execute against. Maps to 409.</summary>
public sealed class NoValidApprovalException(Guid caseId) : Exception($"Case '{caseId}' has no currently-valid approval to execute against.")
{
}

/// <summary>Same Idempotency-Key reused with a different command hash. Maps to 409 (BR-018/019).</summary>
public sealed class IdempotencyConflictException(string idempotencyKey)
    : Exception($"Idempotency-Key '{idempotencyKey}' was already used with a different request payload.")
{
}

/// <summary>The execution is not awaiting reconciliation. Maps to 409.</summary>
public sealed class ExecutionNotAwaitingReconciliationException(Guid executionId)
    : Exception($"Execution '{executionId}' is not awaiting reconciliation.")
{
}
