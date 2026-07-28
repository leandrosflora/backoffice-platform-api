using Backoffice.Application.Abstractions;
using Backoffice.Application.Approvals;
using Backoffice.Application.Cases;
using Backoffice.Domain.Approvals;
using Backoffice.Domain.Cases;
using Backoffice.Domain.Executions;

namespace Backoffice.Application.Executions;

public sealed record RequestExecutionResult(ExecutionResponse Execution, bool IsReplay);

public sealed class RequestExecutionHandler(
    ICaseRepository caseRepository,
    IApprovalRepository approvalRepository,
    IExecutionRepository executionRepository,
    IIdempotencyRecordRepository idempotencyRepository,
    IExecutionGateway gateway,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<RequestExecutionResult> HandleAsync(
        string tenantId,
        Guid caseId,
        string idempotencyKey,
        RequestExecutionRequest request,
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

        var existingRecord = await idempotencyRepository.FindAsync(tenantId, caseId, idempotencyKey, cancellationToken);
        if (existingRecord is not null)
        {
            if (existingRecord.CommandHash != request.CommandHash)
            {
                throw new IdempotencyConflictException(idempotencyKey);
            }

            var existingExecution = await executionRepository.FindByIdAsync(tenantId, caseId, existingRecord.ExecutionId, cancellationToken)
                ?? throw new ExecutionNotFoundException(existingRecord.ExecutionId);
            return new RequestExecutionResult(existingExecution.ToResponse(), IsReplay: true);
        }

        var approval = await approvalRepository.FindByIdAsync(tenantId, caseId, request.ApprovalId, cancellationToken);
        var approvalIsValid = approval is not null
            && approval.Status == ApprovalStatus.Approved
            && (approval.ExpiresAt is null || approval.ExpiresAt > clock.UtcNow)
            && approval.RecommendationVersion == request.RecommendationVersion
            && @case.ApprovedRecommendationVersion == request.RecommendationVersion
            && @case.State == CaseState.Approved;

        if (!approvalIsValid)
        {
            throw new NoValidApprovalException(caseId);
        }

        var execution = Execution.Create(caseId, tenantId, idempotencyKey, request.CommandHash, clock.UtcNow);
        executionRepository.Add(execution);
        idempotencyRepository.Add(IdempotencyRecord.Create(tenantId, caseId, idempotencyKey, request.CommandHash, execution.ExecutionId, clock.UtcNow));

        @case.Transition(
            @case.CaseVersion, CaseState.ExecutionPending, "ExecutionRequested", actorId, "execution",
            correlationId, null, "Governed execution requested.", clock.UtcNow);

        // Durably record the pending execution + idempotency mapping before calling the
        // gateway, so a crash mid-call never leaves a retry able to double-submit.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var result = await gateway.ExecuteAsync(new(caseId, request.CommandType, request.CommandHash), cancellationToken);

        switch (result.Outcome)
        {
            case ExecutionOutcome.Succeeded:
                execution.MarkSucceeded(result.ExternalReference, clock.UtcNow);
                @case.Transition(
                    @case.CaseVersion, CaseState.Executed, "ExecutionCompleted", actorId, "execution",
                    correlationId, null, "Execution succeeded.", clock.UtcNow);
                break;
            case ExecutionOutcome.Failed:
                execution.MarkFailed(clock.UtcNow);
                @case.Transition(
                    @case.CaseVersion, CaseState.Failed, "ExecutionFailed", actorId, "execution",
                    correlationId, null, "Execution failed.", clock.UtcNow);
                break;
            case ExecutionOutcome.Ambiguous:
                // Never a silent success and never auto-retried under a new key — reconciliation
                // is the only path forward (spec: governed-execution).
                execution.MarkReconciliationRequired(clock.UtcNow);
                @case.Transition(
                    @case.CaseVersion, CaseState.ReconciliationRequired, "ReconciliationRequired", actorId, "execution",
                    correlationId, null, "Execution result was ambiguous; reconciliation required.", clock.UtcNow);
                break;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new RequestExecutionResult(execution.ToResponse(), IsReplay: false);
    }
}
