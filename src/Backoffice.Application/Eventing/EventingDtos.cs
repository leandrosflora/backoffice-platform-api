using Backoffice.Domain.Eventing;

namespace Backoffice.Application.Eventing;

public sealed record ScheduleTimerRequest(string TimerType, int DelaySeconds, Dictionary<string, object?>? Payload);

public sealed record ReplayDeadLetterRequest(string Reason);

public sealed record TimerResponse(
    Guid TimerId,
    string TenantId,
    Guid AggregateId,
    string TimerType,
    DateTimeOffset DueAt,
    TimerStatus Status,
    int Attempts,
    DateTimeOffset? FiredAt);

public sealed record OutboxMessageResponse(
    long Id,
    Guid EventId,
    Guid AggregateId,
    string TenantId,
    string EventType,
    string Topic,
    OutboxStatus Status,
    int Attempts,
    DateTimeOffset? PublishedAt,
    string? LastError);

public sealed record DeadLetterResponse(
    long Id,
    string Source,
    string SourceTopic,
    Guid EventId,
    string TenantId,
    Guid AggregateId,
    string EventType,
    string Error,
    int Attempts,
    DeadLetterStatus Status,
    DateTimeOffset FailedAt,
    DateTimeOffset? ReplayedAt,
    Guid? ReplayEventId);

public sealed record ReplayDeadLetterResponse(long DeadLetterId, Guid ReplayEventId, string Status);

public static class EventingMapping
{
    public static TimerResponse ToResponse(this EventTimer timer) => new(
        timer.TimerId, timer.TenantId, timer.AggregateId, timer.TimerType, timer.DueAt, timer.Status, timer.Attempts, timer.FiredAt);

    public static OutboxMessageResponse ToResponse(this OutboxMessage message) => new(
        message.Id, message.EventId, message.AggregateId, message.TenantId, message.EventType, message.Topic,
        message.Status, message.Attempts, message.PublishedAt, message.LastError);

    public static DeadLetterResponse ToResponse(this DeadLetter deadLetter) => new(
        deadLetter.Id, deadLetter.Source, deadLetter.SourceTopic, deadLetter.EventId, deadLetter.TenantId,
        deadLetter.AggregateId, deadLetter.EventType, deadLetter.Error, deadLetter.Attempts, deadLetter.Status,
        deadLetter.FailedAt, deadLetter.ReplayedAt, deadLetter.ReplayEventId);
}
