using Backoffice.Domain.Executions;

namespace Backoffice.Application.Executions;

public sealed record RequestExecutionRequest(
    long CaseVersion,
    Guid ApprovalId,
    long RecommendationVersion,
    CommandType CommandType,
    string CommandHash,
    IReadOnlyList<Guid> EvidenceReferences);

public sealed record ResolveReconciliationRequest(long CaseVersion, ReconciliationResolution Resolution, string Reason);

public sealed record ExecutionResponse(
    Guid ExecutionId,
    Guid CaseId,
    ExecutionStatus Status,
    string IdempotencyKey,
    string CommandHash,
    string? ExternalReference,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

public static class ExecutionMapping
{
    public static ExecutionResponse ToResponse(this Execution execution) => new(
        execution.ExecutionId,
        execution.CaseId,
        execution.Status,
        execution.IdempotencyKey,
        execution.CommandHash,
        execution.ExternalReference,
        execution.CreatedAt,
        execution.CompletedAt);
}
