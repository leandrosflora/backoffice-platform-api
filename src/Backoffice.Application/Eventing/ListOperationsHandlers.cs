using Backoffice.Application.Policy;

namespace Backoffice.Application.Eventing;

/// <summary>Common shape for the three read-only operator listings — same action/purpose,
/// only the resource id and backing repository differ (spec: eventing-reliability).</summary>
file static class OperationsAuthorization
{
    public static Task AuthorizeReadAsync(
        PolicyEnforcer policyEnforcer, string tenantId, string resourceId, string actorId,
        IReadOnlyList<string> roles, string subjectType, Guid correlationId, CancellationToken cancellationToken) =>
        policyEnforcer.EnforceAsync(new AuthorizationInput(
            new PolicySubject(actorId, subjectType, tenantId, roles),
            PolicyActions.EventRead,
            new PolicyResource(PolicyResourceTypes.Event, resourceId, tenantId, "OPERATIONAL"),
            PolicyPurposes.Operations,
            correlationId.ToString(),
            new Dictionary<string, object?>()),
            cancellationToken: cancellationToken);
}

public sealed class ListOutboxHandler(IOutboxRepository outboxRepository, PolicyEnforcer policyEnforcer)
{
    public async Task<IReadOnlyList<OutboxMessageResponse>> HandleAsync(
        string tenantId, int limit, string actorId, IReadOnlyList<string> roles, string subjectType,
        Guid correlationId, CancellationToken cancellationToken = default)
    {
        await OperationsAuthorization.AuthorizeReadAsync(policyEnforcer, tenantId, "outbox", actorId, roles, subjectType, correlationId, cancellationToken);
        var messages = await outboxRepository.ListByTenantAsync(tenantId, limit, cancellationToken);
        return messages.Select(m => m.ToResponse()).ToList();
    }
}

public sealed class ListDeadLettersHandler(IDeadLetterRepository deadLetterRepository, PolicyEnforcer policyEnforcer)
{
    public async Task<IReadOnlyList<DeadLetterResponse>> HandleAsync(
        string tenantId, int limit, string actorId, IReadOnlyList<string> roles, string subjectType,
        Guid correlationId, CancellationToken cancellationToken = default)
    {
        await OperationsAuthorization.AuthorizeReadAsync(policyEnforcer, tenantId, "dead-letters", actorId, roles, subjectType, correlationId, cancellationToken);
        var deadLetters = await deadLetterRepository.ListByTenantAsync(tenantId, limit, cancellationToken);
        return deadLetters.Select(d => d.ToResponse()).ToList();
    }
}

public sealed class ListTimersHandler(ITimerRepository timerRepository, PolicyEnforcer policyEnforcer)
{
    public async Task<IReadOnlyList<TimerResponse>> HandleAsync(
        string tenantId, int limit, string actorId, IReadOnlyList<string> roles, string subjectType,
        Guid correlationId, CancellationToken cancellationToken = default)
    {
        await OperationsAuthorization.AuthorizeReadAsync(policyEnforcer, tenantId, "timers", actorId, roles, subjectType, correlationId, cancellationToken);
        var timers = await timerRepository.ListByTenantAsync(tenantId, limit, cancellationToken);
        return timers.Select(t => t.ToResponse()).ToList();
    }
}
