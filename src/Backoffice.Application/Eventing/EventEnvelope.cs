using System.Text.Json;
using System.Text.Json.Serialization;
using Backoffice.Domain.Eventing;

namespace Backoffice.Application.Eventing;

/// <summary>
/// The wire shape published to Kafka and stored verbatim in a dead letter's envelope_json,
/// matching the Python sample's `build_envelope` (camelCase — this is the actual published
/// contract, distinct from the snake_case OPA input DTOs in Policy/). Field names mirror
/// contracts/schemas/event-envelope.yaml's envelope wrapper.
/// </summary>
public sealed record EventEnvelope(
    [property: JsonPropertyName("eventId")] Guid EventId,
    [property: JsonPropertyName("eventType")] string EventType,
    [property: JsonPropertyName("eventVersion")] int EventVersion,
    [property: JsonPropertyName("occurredAt")] DateTimeOffset OccurredAt,
    [property: JsonPropertyName("tenantId")] string TenantId,
    [property: JsonPropertyName("caseId")] Guid CaseId,
    [property: JsonPropertyName("correlationId")] Guid CorrelationId,
    [property: JsonPropertyName("causationId")] Guid? CausationId,
    [property: JsonPropertyName("producer")] string Producer,
    [property: JsonPropertyName("dataClassification")] string DataClassification,
    [property: JsonPropertyName("replayCount")] int ReplayCount,
    [property: JsonPropertyName("replayOf")] Guid? ReplayOf,
    [property: JsonPropertyName("payload")] JsonElement Payload)
{
    public static EventEnvelope FromOutboxMessage(OutboxMessage message) => new(
        message.EventId,
        message.EventType,
        1,
        message.CreatedAt,
        message.TenantId,
        message.AggregateId,
        message.CorrelationId,
        message.CausationId,
        message.Producer,
        "INTERNAL",
        message.ReplayCount,
        message.ReplayOf,
        JsonDocument.Parse(message.PayloadJson).RootElement.Clone());
}
