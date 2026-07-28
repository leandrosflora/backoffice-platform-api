using Backoffice.Application.Abstractions;
using Backoffice.Application.Policy;

namespace Backoffice.Application.Cases;

public sealed class GetCaseHandler(ICaseRepository repository, IUnitOfWork unitOfWork, IClock clock, PolicyEnforcer policyEnforcer)
{
    public async Task<CaseResponse> HandleAsync(
        string tenantId,
        Guid caseId,
        string actorId,
        IReadOnlyList<string> roles,
        string subjectType,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        var @case = await repository.FindByIdAsync(tenantId, caseId, cancellationToken)
            ?? throw new CaseNotFoundException(caseId);

        await policyEnforcer.EnforceAsync(new AuthorizationInput(
            new PolicySubject(actorId, subjectType, tenantId, roles),
            PolicyActions.CaseRead,
            new PolicyResource(PolicyResourceTypes.Case, caseId.ToString(), tenantId, @case.State.ToWireString()),
            PolicyPurposes.CaseProcessing,
            correlationId.ToString(),
            []), cancellationToken: cancellationToken);

        // On-read expiry evaluation: stands in for a dedicated background worker until
        // section 8 (spec: human-approval, "More-evidence-required loop and approval expiry").
        if (@case.ExpireApprovalIfDue(clock.UtcNow, Guid.NewGuid()))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return @case.ToResponse();
    }
}

public sealed class ListCasesHandler(ICaseRepository repository, PolicyEnforcer policyEnforcer)
{
    public async Task<IReadOnlyList<CaseResponse>> HandleAsync(
        string tenantId,
        string actorId,
        IReadOnlyList<string> roles,
        string subjectType,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        await policyEnforcer.EnforceAsync(new AuthorizationInput(
            new PolicySubject(actorId, subjectType, tenantId, roles),
            PolicyActions.CaseRead,
            new PolicyResource(PolicyResourceTypes.Case, "list", tenantId),
            PolicyPurposes.CaseProcessing,
            correlationId.ToString(),
            []), cancellationToken: cancellationToken);

        var cases = await repository.ListAsync(tenantId, cancellationToken);
        return cases.Select(c => c.ToResponse()).ToList();
    }
}

public sealed class GetCaseTimelineHandler(ICaseRepository repository, PolicyEnforcer policyEnforcer)
{
    public async Task<IReadOnlyList<TimelineEntryResponse>> HandleAsync(
        string tenantId,
        Guid caseId,
        string actorId,
        IReadOnlyList<string> roles,
        string subjectType,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        var @case = await repository.FindByIdAsync(tenantId, caseId, cancellationToken)
            ?? throw new CaseNotFoundException(caseId);

        await policyEnforcer.EnforceAsync(new AuthorizationInput(
            new PolicySubject(actorId, subjectType, tenantId, roles),
            PolicyActions.AuditRead,
            new PolicyResource(PolicyResourceTypes.Audit, caseId.ToString(), tenantId, @case.State.ToWireString()),
            PolicyPurposes.Audit,
            correlationId.ToString(),
            []), cancellationToken: cancellationToken);

        return @case.Timeline
            .OrderBy(t => t.CaseVersion)
            .Select(t => t.ToResponse())
            .ToList();
    }
}
