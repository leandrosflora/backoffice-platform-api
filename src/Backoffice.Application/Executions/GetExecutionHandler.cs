using Backoffice.Application.Cases;
using Backoffice.Application.Policy;

namespace Backoffice.Application.Executions;

public sealed class GetExecutionHandler(ICaseRepository caseRepository, IExecutionRepository executionRepository, PolicyEnforcer policyEnforcer)
{
    public async Task<ExecutionResponse> HandleAsync(
        string tenantId,
        Guid caseId,
        Guid executionId,
        string actorId,
        IReadOnlyList<string> roles,
        string subjectType,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        var @case = await caseRepository.FindByIdAsync(tenantId, caseId, cancellationToken)
            ?? throw new CaseNotFoundException(caseId);

        var execution = await executionRepository.FindByIdAsync(tenantId, caseId, executionId, cancellationToken)
            ?? throw new ExecutionNotFoundException(executionId);

        await policyEnforcer.EnforceAsync(new AuthorizationInput(
            new PolicySubject(actorId, subjectType, tenantId, roles),
            PolicyActions.ExecutionRead,
            new PolicyResource(PolicyResourceTypes.Execution, executionId.ToString(), tenantId, @case.State.ToWireString()),
            PolicyPurposes.Execution,
            correlationId.ToString(),
            []), cancellationToken: cancellationToken);

        return execution.ToResponse();
    }
}

public sealed class ListExecutionsHandler(ICaseRepository caseRepository, IExecutionRepository executionRepository, PolicyEnforcer policyEnforcer)
{
    public async Task<IReadOnlyList<ExecutionResponse>> HandleAsync(
        string tenantId,
        Guid caseId,
        string actorId,
        IReadOnlyList<string> roles,
        string subjectType,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        var @case = await caseRepository.FindByIdAsync(tenantId, caseId, cancellationToken)
            ?? throw new CaseNotFoundException(caseId);

        await policyEnforcer.EnforceAsync(new AuthorizationInput(
            new PolicySubject(actorId, subjectType, tenantId, roles),
            PolicyActions.ExecutionRead,
            new PolicyResource(PolicyResourceTypes.Execution, "list", tenantId, @case.State.ToWireString()),
            PolicyPurposes.Execution,
            correlationId.ToString(),
            []), cancellationToken: cancellationToken);

        var executions = await executionRepository.ListByCaseAsync(tenantId, caseId, cancellationToken);
        return executions.Select(e => e.ToResponse()).ToList();
    }
}
