using Backoffice.Application.Abstractions;
using Backoffice.Application.Cases;
using Backoffice.Application.Policy;
using Backoffice.Domain.Cases;
using Backoffice.Domain.Executions;

namespace Backoffice.Application.Executions;

public sealed class ResolveReconciliationHandler(
    ICaseRepository caseRepository,
    IExecutionRepository executionRepository,
    IUnitOfWork unitOfWork,
    IClock clock,
    PolicyEnforcer policyEnforcer)
{
    public async Task<ExecutionResponse> HandleAsync(
        string tenantId,
        Guid caseId,
        Guid executionId,
        ResolveReconciliationRequest request,
        string actorId,
        IReadOnlyList<string> roles,
        string subjectType,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        var @case = await caseRepository.FindByIdAsync(tenantId, caseId, cancellationToken)
            ?? throw new CaseNotFoundException(caseId);

        if (request.CaseVersion != @case.CaseVersion)
        {
            throw new CaseVersionConflictException(request.CaseVersion, @case.CaseVersion);
        }

        var execution = await executionRepository.FindByIdAsync(tenantId, caseId, executionId, cancellationToken)
            ?? throw new ExecutionNotFoundException(executionId);

        if (execution.Status != ExecutionStatus.ReconciliationRequired)
        {
            throw new ExecutionNotAwaitingReconciliationException(executionId);
        }

        // policies/authorization.rego's purpose_matches_action has no dedicated bucket for
        // reconciliation.resolve (it isn't in operation_action/audit_action/approval_action/
        // execution_action), so it falls into the default clause requiring CASE_MANAGEMENT
        // or CASE_PROCESSING — PolicyPurposes.Reconciliation ("RECONCILIATION") would never match.
        await policyEnforcer.EnforceAsync(new AuthorizationInput(
            new PolicySubject(actorId, subjectType, tenantId, roles),
            PolicyActions.ReconciliationResolve,
            new PolicyResource(PolicyResourceTypes.Execution, executionId.ToString(), tenantId, @case.State.ToWireString()),
            PolicyPurposes.CaseProcessing,
            correlationId.ToString(),
            new Dictionary<string, object?> { ["case_version"] = @case.CaseVersion }),
            new Dictionary<string, bool> { ["verify-case-version"] = true },
            cancellationToken);

        execution.Reconcile(request.Resolution, clock.UtcNow);

        var toState = request.Resolution switch
        {
            ReconciliationResolution.ConfirmedSucceeded => CaseState.Executed,
            ReconciliationResolution.ConfirmedFailed => CaseState.Failed,
            ReconciliationResolution.Escalated => (CaseState?)null,
            _ => throw new ArgumentOutOfRangeException(),
        };

        if (toState is not null)
        {
            @case.Transition(
                @case.CaseVersion, toState.Value, "ReconciliationResolved", actorId, "reconciliation",
                correlationId, null, request.Reason, clock.UtcNow,
                BusinessRuleReferences.ReconciliationRequired, PolicyActions.ReconciliationResolve);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return execution.ToResponse();
    }
}
