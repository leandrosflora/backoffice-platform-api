using Backoffice.Application.Abstractions;
using Backoffice.Application.Approvals;
using Backoffice.Application.Cases;
using Backoffice.Application.Observability;
using Backoffice.Application.Policy;
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
    IClock clock,
    PolicyEnforcer policyEnforcer)
{
    public async Task<RequestExecutionResult> HandleAsync(
        string tenantId,
        Guid caseId,
        string idempotencyKey,
        RequestExecutionRequest request,
        string actorId,
        IReadOnlyList<string> roles,
        string subjectType,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        var @case = await caseRepository.FindByIdAsync(tenantId, caseId, cancellationToken)
            ?? throw new CaseNotFoundException(caseId);

        // Idempotency replay is checked before the case-version check: a replay of an
        // already-processed request must still return the original result even after the
        // case has since moved on (e.g. the execution already completed and advanced the
        // case's version) — only a genuinely new request is held to the caller's expected
        // version (spec: governed-execution, "Idempotent execution").
        var existingRecord = await idempotencyRepository.FindAsync(tenantId, caseId, idempotencyKey, cancellationToken);
        if (existingRecord is not null)
        {
            if (existingRecord.CommandHash != request.CommandHash)
            {
                RecordIdempotency("conflict");
                throw new IdempotencyConflictException(idempotencyKey);
            }

            var existingExecution = await executionRepository.FindByIdAsync(tenantId, caseId, existingRecord.ExecutionId, cancellationToken)
                ?? throw new ExecutionNotFoundException(existingRecord.ExecutionId);
            RecordIdempotency("replay");
            return new RequestExecutionResult(existingExecution.ToResponse(), IsReplay: true);
        }

        if (request.CaseVersion != @case.CaseVersion)
        {
            throw new CaseVersionConflictException(request.CaseVersion, @case.CaseVersion);
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

        await policyEnforcer.EnforceAsync(new AuthorizationInput(
            new PolicySubject(actorId, subjectType, tenantId, roles),
            PolicyActions.ExecutionRequest,
            new PolicyResource(PolicyResourceTypes.Execution, caseId.ToString(), tenantId, @case.State.ToWireString()),
            PolicyPurposes.Execution,
            correlationId.ToString(),
            new Dictionary<string, object?>
            {
                ["case_version"] = @case.CaseVersion,
                ["approval_status"] = "APPROVED",
                ["approval_valid"] = true,
                ["recommendation_version"] = request.RecommendationVersion,
                ["approved_recommendation_version"] = @case.ApprovedRecommendationVersion,
                ["idempotency_key"] = idempotencyKey,
                ["command_hash"] = request.CommandHash,
                ["evidence_references"] = request.EvidenceReferences.Select(id => id.ToString()).ToList(),
            }),
            new Dictionary<string, bool>
            {
                ["verify-case-version"] = true,
                ["verify-approval"] = true,
                ["verify-idempotency"] = !string.IsNullOrEmpty(idempotencyKey),
                ["verify-evidence"] = request.EvidenceReferences.Count > 0,
            },
            cancellationToken);

        RecordIdempotency("new");
        var execution = Execution.Create(caseId, tenantId, idempotencyKey, request.CommandHash, clock.UtcNow);
        executionRepository.Add(execution);
        idempotencyRepository.Add(IdempotencyRecord.Create(tenantId, caseId, idempotencyKey, request.CommandHash, execution.ExecutionId, clock.UtcNow));

        @case.Transition(
            @case.CaseVersion, CaseState.ExecutionPending, "ExecutionRequested", actorId, "execution",
            correlationId, null, "Governed execution requested.", clock.UtcNow,
            BusinessRuleReferences.ExecutionRequested, PolicyActions.ExecutionRequest);

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
                    correlationId, null, "Execution succeeded.", clock.UtcNow,
                    BusinessRuleReferences.ExecutionCompleted, PolicyActions.ExecutionRequest);
                RecordExecutionResult("succeeded");
                break;
            case ExecutionOutcome.Failed:
                execution.MarkFailed(clock.UtcNow);
                @case.Transition(
                    @case.CaseVersion, CaseState.Failed, "ExecutionFailed", actorId, "execution",
                    correlationId, null, "Execution failed.", clock.UtcNow,
                    BusinessRuleReferences.ExecutionFailed, PolicyActions.ExecutionRequest);
                RecordExecutionResult("failed");
                break;
            case ExecutionOutcome.Ambiguous:
                // Never a silent success and never auto-retried under a new key — reconciliation
                // is the only path forward (spec: governed-execution).
                execution.MarkReconciliationRequired(clock.UtcNow);
                @case.Transition(
                    @case.CaseVersion, CaseState.ReconciliationRequired, "ReconciliationRequired", actorId, "execution",
                    correlationId, null, "Execution result was ambiguous; reconciliation required.", clock.UtcNow,
                    BusinessRuleReferences.ReconciliationRequired, PolicyActions.ExecutionRequest);
                RecordExecutionResult("ambiguous");
                ApplicationMetrics.ReconciliationsTotal.Add(1);
                break;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new RequestExecutionResult(execution.ToResponse(), IsReplay: false);
    }

    private static void RecordExecutionResult(string result) =>
        ApplicationMetrics.ExecutionsTotal.Add(1, new KeyValuePair<string, object?>("result", result));

    private static void RecordIdempotency(string result) =>
        ApplicationMetrics.IdempotencyTotal.Add(1,
            new KeyValuePair<string, object?>("action", PolicyActions.ExecutionRequest),
            new KeyValuePair<string, object?>("result", result));
}
