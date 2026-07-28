using Backoffice.Domain.Executions;

namespace Backoffice.Application.Executions;

public enum ExecutionOutcome
{
    Succeeded,
    Failed,
    Ambiguous,
}

public sealed record ExecutionGatewayResult(ExecutionOutcome Outcome, string? ExternalReference);

public sealed record ExecutionCommand(Guid CaseId, CommandType CommandType, string CommandHash);

/// <summary>
/// Pluggable seam for the real system-of-record call a governed execution ultimately makes.
/// Production deployments plug in a real adapter; local/dev/test use
/// <see cref="Backoffice.Infrastructure.Executions.MockExecutionGateway"/>.
/// </summary>
public interface IExecutionGateway
{
    Task<ExecutionGatewayResult> ExecuteAsync(ExecutionCommand command, CancellationToken cancellationToken = default);
}
