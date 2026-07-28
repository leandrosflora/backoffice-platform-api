namespace Backoffice.Domain.Eventing;

/// <summary>
/// Marks an `(consumerName, eventId)` pair as already processed, so at-least-once Kafka
/// redelivery is a no-op rather than a duplicate side effect (spec: eventing-reliability,
/// "Consumer inbox deduplication").
/// </summary>
public sealed class InboxRecord
{
    public string ConsumerName { get; private init; } = string.Empty;
    public Guid EventId { get; private init; }
    public DateTimeOffset ProcessedAt { get; private init; }
    public string ResultJson { get; private init; } = "{}";

    private InboxRecord() { }

    public static InboxRecord Create(string consumerName, Guid eventId, string resultJson, DateTimeOffset now) => new()
    {
        ConsumerName = consumerName,
        EventId = eventId,
        ProcessedAt = now,
        ResultJson = resultJson,
    };
}
