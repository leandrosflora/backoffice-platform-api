using System.Globalization;
using Backoffice.Application.Abstractions;
using Backoffice.Domain.Cases;
using Backoffice.Domain.Common;

namespace Backoffice.Application.Cases;

public sealed class CreateCaseHandler(ICaseRepository repository, IUnitOfWork unitOfWork, IClock clock)
{
    /// <summary>
    /// Idempotent case intake keyed by (tenantId, externalReference): a repeat
    /// submission returns the existing case unchanged (spec: case-management,
    /// "Idempotent case intake").
    /// </summary>
    public async Task<CaseResponse> HandleAsync(
        string tenantId,
        CreateCaseRequest request,
        string actorId,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        var existing = await repository.FindByExternalReferenceAsync(tenantId, request.ExternalReference, cancellationToken);
        if (existing is not null)
        {
            return existing.ToResponse();
        }

        var @case = Case.Create(
            tenantId,
            request.ExternalReference,
            request.DisputeType,
            request.Channel,
            request.Priority,
            new Money(request.DisputedAmount.Currency, decimal.Parse(request.DisputedAmount.Amount, CultureInfo.InvariantCulture)),
            correlationId,
            actorId,
            clock.UtcNow);

        repository.Add(@case);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return @case.ToResponse();
    }
}
