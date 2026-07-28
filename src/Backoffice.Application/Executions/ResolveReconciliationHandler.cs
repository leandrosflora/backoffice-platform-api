using Backoffice.Application.Abstractions;
using Backoffice.Application.Cases;
using Backoffice.Domain.Cases;
using Backoffice.Domain.Executions;

namespace Backoffice.Application.Executions;

public sealed class ResolveReconciliationHandler(
    ICaseRepository caseRepository,
    IExecutionRepository executionRepository,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<ExecutionResponse> HandleAsync(
        string tenantId,
        Guid caseId,
        Guid executionId,
        ResolveReconciliationRequest request,
        string actorId,
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
                correlationId, null, request.Reason, clock.UtcNow);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return execution.ToResponse();
    }
}
