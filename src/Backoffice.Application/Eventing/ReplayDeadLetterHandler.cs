using System.Text.Json;
using Backoffice.Application.Abstractions;
using Backoffice.Application.Policy;
using Backoffice.Domain.Eventing;

namespace Backoffice.Application.Eventing;

public sealed class ReplayDeadLetterHandler(
    IDeadLetterRepository deadLetterRepository,
    IOutboxRepository outboxRepository,
    IReplayAuditRepository replayAuditRepository,
    IUnitOfWork unitOfWork,
    IClock clock,
    PolicyEnforcer policyEnforcer)
{
    public async Task<ReplayDeadLetterResponse> HandleAsync(
        string tenantId,
        long deadLetterId,
        ReplayDeadLetterRequest request,
        string actorId,
        IReadOnlyList<string> roles,
        string subjectType,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        var deadLetter = await deadLetterRepository.FindByIdAsync(deadLetterId, cancellationToken);
        if (deadLetter is null || deadLetter.TenantId != tenantId)
        {
            throw new DeadLetterNotFoundException(deadLetterId);
        }

        await policyEnforcer.EnforceAsync(new AuthorizationInput(
            new PolicySubject(actorId, subjectType, tenantId, roles),
            PolicyActions.EventReplay,
            new PolicyResource(PolicyResourceTypes.DeadLetter, deadLetterId.ToString(), tenantId, deadLetter.Status.ToWireString()),
            PolicyPurposes.Operations,
            correlationId.ToString(),
            new Dictionary<string, object?>
            {
                ["reason"] = request.Reason,
                ["source"] = "DEAD_LETTER",
            }),
            cancellationToken: cancellationToken);

        // Domain-level guard in addition to OPA's resource.state == "OPEN" check above:
        // a concurrent replay of the same dead letter between the check and this point
        // must still be rejected, not silently accepted.
        if (deadLetter.Status != DeadLetterStatus.Open)
        {
            throw new DeadLetterAlreadyReplayedException(deadLetterId);
        }

        var originalEnvelope = JsonDocument.Parse(deadLetter.EnvelopeJson);
        var payloadJson = originalEnvelope.RootElement.TryGetProperty("payload", out var payloadElement)
            ? payloadElement.GetRawText()
            : "{}";
        var originalReplayCount = originalEnvelope.RootElement.TryGetProperty("replayCount", out var replayCountElement)
            ? replayCountElement.GetInt32()
            : 0;

        var replay = OutboxMessage.CreateReplay(
            deadLetter.AggregateId, tenantId, deadLetter.EventType, deadLetter.SourceTopic,
            deadLetter.AggregateId.ToString(), correlationId, deadLetter.EventId, originalReplayCount + 1, payloadJson, clock.UtcNow);

        outboxRepository.Add(replay);
        deadLetter.MarkReplayed(replay.EventId, request.Reason, clock.UtcNow);

        var auditEntry = ReplayAuditEntry.Create(
            deadLetterId, deadLetter.EventId, replay.EventId, tenantId, actorId, request.Reason, correlationId, clock.UtcNow);
        replayAuditRepository.Add(auditEntry);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new ReplayDeadLetterResponse(deadLetterId, replay.EventId, "REPLAYED");
    }
}
