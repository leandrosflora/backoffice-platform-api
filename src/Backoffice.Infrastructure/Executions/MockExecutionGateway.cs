using Backoffice.Application.Executions;

namespace Backoffice.Infrastructure.Executions;

/// <summary>
/// Local dev/test execution gateway: deterministic based on recognizable markers in the
/// command hash, so tests can exercise every outcome without a real system-of-record call.
/// Production deployments must replace this with a real adapter implementing
/// <see cref="IExecutionGateway"/>.
/// </summary>
public sealed class MockExecutionGateway : IExecutionGateway
{
    public Task<ExecutionGatewayResult> ExecuteAsync(ExecutionCommand command, CancellationToken cancellationToken = default)
    {
        if (command.CommandHash.Contains("ambiguous", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new ExecutionGatewayResult(ExecutionOutcome.Ambiguous, null));
        }

        if (command.CommandHash.Contains("fail", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new ExecutionGatewayResult(ExecutionOutcome.Failed, null));
        }

        return Task.FromResult(new ExecutionGatewayResult(ExecutionOutcome.Succeeded, $"mock-ext-ref-{Guid.NewGuid():N}"));
    }
}
