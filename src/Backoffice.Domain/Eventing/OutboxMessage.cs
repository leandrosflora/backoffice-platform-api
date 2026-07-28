namespace Backoffice.Domain.Eventing;

public enum OutboxStatus
{
    Pending,
    InFlight,
    Retry,
    Published,
    DeadLetter,
}

/// <summary>
/// A domain event queued for publication to the event backbone. Written in the same
/// transaction as the aggregate/timeline change that produced it (transactional outbox
/// pattern), replacing the Python sample's SQLite `timeline_to_outbox` trigger with the
/// application-level hook in <see cref="Infrastructure.Persistence.BackofficeDbContext"/>
/// (spec: eventing-reliability, "Transactional outbox for domain events").
/// </summary>
public sealed class OutboxMessage
{
    public long Id { get; private init; }
    public Guid EventId { get; private init; }
    public Guid AggregateId { get; private init; }
    public string TenantId { get; private init; } = string.Empty;
    public string EventType { get; private init; } = string.Empty;
    public string Topic { get; private init; } = string.Empty;
    public string MessageKey { get; private init; } = string.Empty;
    public Guid CorrelationId { get; private init; }
    public Guid? CausationId { get; private init; }
    public string Producer { get; private init; } = string.Empty;
    public Guid? ReplayOf { get; private init; }
    public int ReplayCount { get; private init; }
    public string PayloadJson { get; private init; } = "{}";
    public OutboxStatus Status { get; private set; } = OutboxStatus.Pending;
    public int Attempts { get; private set; }
    public DateTimeOffset AvailableAt { get; private set; }
    public DateTimeOffset? LockedAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset CreatedAt { get; private init; }

    private OutboxMessage() { }

    public static OutboxMessage Create(
        Guid aggregateId,
        string tenantId,
        string eventType,
        string topic,
        string messageKey,
        Guid correlationId,
        Guid? causationId,
        string producer,
        string payloadJson,
        DateTimeOffset now) => new()
    {
        EventId = Guid.NewGuid(),
        AggregateId = aggregateId,
        TenantId = tenantId,
        EventType = eventType,
        Topic = topic,
        MessageKey = messageKey,
        CorrelationId = correlationId,
        CausationId = causationId,
        Producer = producer,
        PayloadJson = payloadJson,
        AvailableAt = now,
        CreatedAt = now,
    };

    /// <summary>
    /// Re-enqueues a dead-lettered event under a fresh event id, preserving its original
    /// payload/topic/content (spec: eventing-reliability, "Human-authorized dead-letter replay").
    /// </summary>
    public static OutboxMessage CreateReplay(
        Guid aggregateId,
        string tenantId,
        string eventType,
        string topic,
        string messageKey,
        Guid correlationId,
        Guid originalEventId,
        int replayCount,
        string payloadJson,
        DateTimeOffset now) => new()
    {
        EventId = Guid.NewGuid(),
        AggregateId = aggregateId,
        TenantId = tenantId,
        EventType = eventType,
        Topic = topic,
        MessageKey = messageKey,
        CorrelationId = correlationId,
        CausationId = originalEventId,
        Producer = "controlled-replay-api",
        ReplayOf = originalEventId,
        ReplayCount = replayCount,
        PayloadJson = payloadJson,
        AvailableAt = now,
        CreatedAt = now,
    };

    public void MarkInFlight(DateTimeOffset now)
    {
        Status = OutboxStatus.InFlight;
        LockedAt = now;
    }

    public void MarkPublished(DateTimeOffset now)
    {
        Status = OutboxStatus.Published;
        PublishedAt = now;
        LockedAt = null;
        LastError = null;
    }

    /// <summary>Records a failed publish attempt, moving to RETRY (with exponential
    /// backoff capped at 60s) or DEAD_LETTER once <paramref name="maxAttempts"/> is reached.
    /// Returns the resulting status name for logging.</summary>
    public string Fail(string error, int maxAttempts, DateTimeOffset now)
    {
        Attempts++;
        LastError = error.Length > 2000 ? error[..2000] : error;
        LockedAt = null;

        if (Attempts >= maxAttempts)
        {
            Status = OutboxStatus.DeadLetter;
            return nameof(OutboxStatus.DeadLetter);
        }

        var delaySeconds = Math.Min(60, Math.Pow(2, Attempts));
        Status = OutboxStatus.Retry;
        AvailableAt = now.AddSeconds(delaySeconds);
        return nameof(OutboxStatus.Retry);
    }

    /// <summary>Reclaims a row stuck IN_FLIGHT past the claim-timeout window (a dispatcher
    /// crashed mid-publish), making it eligible for another attempt.</summary>
    public void ReclaimIfStale(TimeSpan staleness, DateTimeOffset now)
    {
        if (Status == OutboxStatus.InFlight && LockedAt is not null && now - LockedAt.Value > staleness)
        {
            Status = OutboxStatus.Retry;
            LockedAt = null;
        }
    }
}
