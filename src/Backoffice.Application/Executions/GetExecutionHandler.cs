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

        // policies/authorization.rego's purpose_matches_action has no dedicated bucket for
        // execution.read (execution_action there means only execution.request), so it falls
        // into the default clause requiring CASE_MANAGEMENT or CASE_PROCESSING —
        // PolicyPurposes.Execution ("EXECUTION") would never match (same category of bug
        // already fixed for reconciliation.resolve in ResolveReconciliationHandler).
        await policyEnforcer.EnforceAsync(new AuthorizationInput(
            new PolicySubject(actorId, subjectType, tenantId, roles),
            PolicyActions.ExecutionRead,
            new PolicyResource(PolicyResourceTypes.Execution, executionId.ToString(), tenantId, @case.State.ToWireString()),
            PolicyPurposes.CaseProcessing,
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

        // Same purpose-binding fix as GetExecutionHandler above.
        await policyEnforcer.EnforceAsync(new AuthorizationInput(
            new PolicySubject(actorId, subjectType, tenantId, roles),
            PolicyActions.ExecutionRead,
            new PolicyResource(PolicyResourceTypes.Execution, "list", tenantId, @case.State.ToWireString()),
            PolicyPurposes.CaseProcessing,
            correlationId.ToString(),
            []), cancellationToken: cancellationToken);

        var executions = await executionRepository.ListByCaseAsync(tenantId, caseId, cancellationToken);
        return executions.Select(e => e.ToResponse()).ToList();
    }
}
