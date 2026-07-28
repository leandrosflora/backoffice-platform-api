using Backoffice.Application.Cases;

namespace Backoffice.Application.Executions;

public sealed class GetExecutionHandler(ICaseRepository caseRepository, IExecutionRepository executionRepository)
{
    public async Task<ExecutionResponse> HandleAsync(
        string tenantId, Guid caseId, Guid executionId, CancellationToken cancellationToken = default)
    {
        _ = await caseRepository.FindByIdAsync(tenantId, caseId, cancellationToken)
            ?? throw new CaseNotFoundException(caseId);

        var execution = await executionRepository.FindByIdAsync(tenantId, caseId, executionId, cancellationToken)
            ?? throw new ExecutionNotFoundException(executionId);

        return execution.ToResponse();
    }
}

public sealed class ListExecutionsHandler(ICaseRepository caseRepository, IExecutionRepository executionRepository)
{
    public async Task<IReadOnlyList<ExecutionResponse>> HandleAsync(
        string tenantId, Guid caseId, CancellationToken cancellationToken = default)
    {
        _ = await caseRepository.FindByIdAsync(tenantId, caseId, cancellationToken)
            ?? throw new CaseNotFoundException(caseId);

        var executions = await executionRepository.ListByCaseAsync(tenantId, caseId, cancellationToken);
        return executions.Select(e => e.ToResponse()).ToList();
    }
}
