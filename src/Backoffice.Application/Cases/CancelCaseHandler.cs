using Backoffice.Application.Abstractions;
using Backoffice.Domain.Cases;

namespace Backoffice.Application.Cases;

public sealed class CancelCaseHandler(ICaseRepository repository, IUnitOfWork unitOfWork, IClock clock)
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
        Guid correlationId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var @case = await repository.FindByIdAsync(tenantId, caseId, cancellationToken)
            ?? throw new CaseNotFoundException(caseId);

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
