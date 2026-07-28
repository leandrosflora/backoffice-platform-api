namespace Backoffice.Domain.Eventing;

public enum DeadLetterStatus
{
    Open,
    Replayed,
}

public sealed class DeadLetterNotFoundException(long deadLetterId)
    : Exception($"Dead letter '{deadLetterId}' was not found.")
{
}

public sealed class DeadLetterAlreadyReplayedException(long deadLetterId)
    : Exception($"Dead letter '{deadLetterId}' was already replayed.")
{
}

/// <summary>
/// A durably recorded event that exhausted retries during outbox publish or consumer
/// processing (or timer firing), preserving the original envelope, error, and attempt
/// count rather than dropping it (spec: eventing-reliability, "Retry with backoff and
/// dead-letter queue").
/// </summary>
public sealed class DeadLetter
{
    public long Id { get; private init; }

    /// <summary>Origin of the failure: "outbox" (dispatch), "consumer" (workflow worker), or "timer".</summary>
    public string Source { get; private init; } = string.Empty;
    public string SourceTopic { get; private init; } = string.Empty;
    public Guid EventId { get; private init; }
    public string TenantId { get; private init; } = string.Empty;
    public Guid AggregateId { get; private init; }
    public string EventType { get; private init; } = string.Empty;
    public string EnvelopeJson { get; private init; } = "{}";
    public string Error { get; private init; } = string.Empty;
    public int Attempts { get; private init; }
    public DeadLetterStatus Status { get; private set; } = DeadLetterStatus.Open;
    public DateTimeOffset FailedAt { get; private init; }
    public DateTimeOffset? ReplayedAt { get; private set; }
    public Guid? ReplayEventId { get; private set; }
    public string? ReplayReason { get; private set; }

    private DeadLetter() { }

    public static DeadLetter Create(
        string source,
        string sourceTopic,
        Guid eventId,
        string tenantId,
        Guid aggregateId,
        string eventType,
        string envelopeJson,
        string error,
        int attempts,
        DateTimeOffset now) => new()
    {
        Source = source,
        SourceTopic = sourceTopic,
        EventId = eventId,
        TenantId = tenantId,
        AggregateId = aggregateId,
        EventType = eventType,
        EnvelopeJson = envelopeJson,
        Error = error.Length > 2000 ? error[..2000] : error,
        Attempts = attempts,
        FailedAt = now,
    };

    public void MarkReplayed(Guid replayEventId, string reason, DateTimeOffset now)
    {
        if (Status != DeadLetterStatus.Open)
        {
            throw new DeadLetterAlreadyReplayedException(Id);
        }

        Status = DeadLetterStatus.Replayed;
        ReplayedAt = now;
        ReplayEventId = replayEventId;
        ReplayReason = reason;
    }
}
