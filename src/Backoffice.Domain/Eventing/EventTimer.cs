namespace Backoffice.Domain.Eventing;

public enum TimerStatus
{
    Scheduled,
    InFlight,
    Retry,
    Fired,
    DeadLetter,
}

/// <summary>
/// A durable, scheduled effect against an aggregate (e.g. `CASE_EXPIRY`), fired by the timer
/// worker once due (spec: eventing-reliability, "Timer scheduling and firing").
/// </summary>
public sealed class EventTimer
{
    public Guid TimerId { get; private init; }
    public string TenantId { get; private init; } = string.Empty;
    public Guid AggregateId { get; private init; }
    public string TimerType { get; private init; } = string.Empty;
    public DateTimeOffset DueAt { get; private set; }
    public string PayloadJson { get; private init; } = "{}";
    public TimerStatus Status { get; private set; } = TimerStatus.Scheduled;
    public int Attempts { get; private set; }
    public DateTimeOffset? LockedAt { get; private set; }
    public DateTimeOffset? FiredAt { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset CreatedAt { get; private init; }

    private EventTimer() { }

    public static EventTimer Schedule(
        string tenantId, Guid aggregateId, string timerType, DateTimeOffset dueAt, string payloadJson, DateTimeOffset now) => new()
    {
        TimerId = Guid.NewGuid(),
        TenantId = tenantId,
        AggregateId = aggregateId,
        TimerType = timerType,
        DueAt = dueAt,
        PayloadJson = payloadJson,
        CreatedAt = now,
    };

    public void MarkInFlight(DateTimeOffset now)
    {
        Status = TimerStatus.InFlight;
        LockedAt = now;
    }

    public void MarkFired(DateTimeOffset now)
    {
        Status = TimerStatus.Fired;
        FiredAt = now;
        LockedAt = null;
        LastError = null;
    }

    public string Fail(string error, int maxAttempts, DateTimeOffset now)
    {
        Attempts++;
        LastError = error.Length > 2000 ? error[..2000] : error;
        LockedAt = null;

        if (Attempts >= maxAttempts)
        {
            Status = TimerStatus.DeadLetter;
            return nameof(TimerStatus.DeadLetter);
        }

        var delaySeconds = Math.Min(60, Math.Pow(2, Attempts));
        Status = TimerStatus.Retry;
        DueAt = now.AddSeconds(delaySeconds);
        return nameof(TimerStatus.Retry);
    }

    public void ReclaimIfStale(TimeSpan staleness, DateTimeOffset now)
    {
        if (Status == TimerStatus.InFlight && LockedAt is not null && now - LockedAt.Value > staleness)
        {
            Status = TimerStatus.Retry;
            LockedAt = null;
        }
    }
}
