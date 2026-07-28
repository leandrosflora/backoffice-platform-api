using Backoffice.Application.Abstractions;
using Backoffice.Application.Policy;
using Backoffice.Domain.Cases;

namespace Backoffice.Application.Cases;

public sealed class CancelCaseHandler(ICaseRepository repository, IUnitOfWork unitOfWork, IClock clock, PolicyEnforcer policyEnforcer)
{
    /// <summary>
    /// Cancels a case, enforcing optimistic concurrency via <paramref name="expectedVersion"/>
    /// (the caller's If-Match header) — mismatch surfaces as CaseVersionConflictException,
    /// which the API layer maps to 409 (spec: case-management, "Optimistic concurrency").
    /// </summary>
    public async Task<CaseResponse> HandleAsync(
        string tenantId,
        Guid caseId,
        long expectedVersion,
        string actorId,
        IReadOnlyList<string> roles,
        string subjectType,
        Guid correlationId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var @case = await repository.FindByIdAsync(tenantId, caseId, cancellationToken)
            ?? throw new CaseNotFoundException(caseId);

        // Explicit pre-check (kept as a specific 409, a data-consistency concern) rather
        // than relying on Case.Transition's own internal check below, since that would only
        // run after the OPA gate — leaving a version mismatch to surface as an opaque
        // obligation failure (403) instead of the correct conflict response.
        if (expectedVersion != @case.CaseVersion)
        {
            throw new CaseVersionConflictException(expectedVersion, @case.CaseVersion);
        }

        await policyEnforcer.EnforceAsync(new AuthorizationInput(
            new PolicySubject(actorId, subjectType, tenantId, roles),
            PolicyActions.CaseCancel,
            new PolicyResource(PolicyResourceTypes.Case, caseId.ToString(), tenantId, @case.State.ToWireString()),
            PolicyPurposes.CaseProcessing,
            correlationId.ToString(),
            new Dictionary<string, object?> { ["case_version"] = expectedVersion }),
            new Dictionary<string, bool> { ["verify-case-version"] = true },
            cancellationToken);

        @case.Transition(
            expectedVersion,
            CaseState.Cancelled,
            "CaseCancelled",
            actorId,
            "case-management",
            correlationId,
            null,
            reason,
            clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return @case.ToResponse();
    }
}
