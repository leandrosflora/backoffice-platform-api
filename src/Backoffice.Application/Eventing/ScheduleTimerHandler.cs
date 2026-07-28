using System.Text.Json;
using Backoffice.Application.Abstractions;
using Backoffice.Application.Cases;
using Backoffice.Application.Policy;
using Backoffice.Domain.Eventing;

namespace Backoffice.Application.Eventing;

public sealed class ScheduleTimerHandler(
    ICaseRepository caseRepository,
    ITimerRepository timerRepository,
    IUnitOfWork unitOfWork,
    IClock clock,
    PolicyEnforcer policyEnforcer)
{
    public async Task<TimerResponse> HandleAsync(
        string tenantId,
        Guid caseId,
        ScheduleTimerRequest request,
        string actorId,
        IReadOnlyList<string> roles,
        string subjectType,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        var @case = await caseRepository.FindByIdAsync(tenantId, caseId, cancellationToken)
            ?? throw new CaseNotFoundException(caseId);

        await policyEnforcer.EnforceAsync(new AuthorizationInput(
            new PolicySubject(actorId, subjectType, tenantId, roles),
            PolicyActions.TimerSchedule,
            new PolicyResource(PolicyResourceTypes.Case, caseId.ToString(), tenantId, @case.State.ToWireString()),
            PolicyPurposes.Operations,
            correlationId.ToString(),
            new Dictionary<string, object?>
            {
                ["timer_type"] = request.TimerType,
                ["delay_seconds"] = request.DelaySeconds,
            }),
            cancellationToken: cancellationToken);

        var dueAt = clock.UtcNow.AddSeconds(request.DelaySeconds);
        var payloadJson = JsonSerializer.Serialize(request.Payload ?? new Dictionary<string, object?>());
        var timer = EventTimer.Schedule(tenantId, caseId, request.TimerType, dueAt, payloadJson, clock.UtcNow);
        timerRepository.Add(timer);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return timer.ToResponse();
    }
}
