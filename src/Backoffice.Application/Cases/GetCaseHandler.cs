using Backoffice.Application.Abstractions;

namespace Backoffice.Application.Cases;

public sealed class GetCaseHandler(ICaseRepository repository, IUnitOfWork unitOfWork, IClock clock)
{
    public async Task<CaseResponse> HandleAsync(string tenantId, Guid caseId, CancellationToken cancellationToken = default)
    {
        var @case = await repository.FindByIdAsync(tenantId, caseId, cancellationToken)
            ?? throw new CaseNotFoundException(caseId);

        // On-read expiry evaluation: stands in for a dedicated background worker until
        // section 8 (spec: human-approval, "More-evidence-required loop and approval expiry").
        if (@case.ExpireApprovalIfDue(clock.UtcNow, Guid.NewGuid()))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return @case.ToResponse();
    }
}

public sealed class ListCasesHandler(ICaseRepository repository)
{
    public async Task<IReadOnlyList<CaseResponse>> HandleAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        var cases = await repository.ListAsync(tenantId, cancellationToken);
        return cases.Select(c => c.ToResponse()).ToList();
    }
}

public sealed class GetCaseTimelineHandler(ICaseRepository repository)
{
    public async Task<IReadOnlyList<TimelineEntryResponse>> HandleAsync(string tenantId, Guid caseId, CancellationToken cancellationToken = default)
    {
        var @case = await repository.FindByIdAsync(tenantId, caseId, cancellationToken)
            ?? throw new CaseNotFoundException(caseId);

        return @case.Timeline
            .OrderBy(t => t.CaseVersion)
            .Select(t => t.ToResponse())
            .ToList();
    }
}
