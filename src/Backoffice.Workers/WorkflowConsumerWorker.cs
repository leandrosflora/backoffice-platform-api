using System.Text.Json;
using Backoffice.Application.Abstractions;
using Backoffice.Application.Audit;
using Backoffice.Application.Cases;
using Backoffice.Application.Eventing;
using Backoffice.Domain.Audit;
using Backoffice.Domain.Eventing;
using Backoffice.Infrastructure.Eventing;
using Confluent.Kafka;
using Microsoft.Extensions.Options;

namespace Backoffice.Workers;

/// <summary>
/// Consumes `backoffice.events.v1`, deduplicating via the inbox, ingesting every event into
/// the append-only audit store (spec: audit-compliance, "Append-only audit ingestion"), and
/// applying the sole consumer-side effect currently defined (a fired `CASE_EXPIRY` timer
/// transitions its case), retrying up to 3 attempts before recording a durable dead letter
/// and publishing to the DLQ topic (spec: eventing-reliability, "Consumer inbox
/// deduplication" and "Retry with backoff and dead-letter queue"). Commits the offset after
/// processed/duplicate/dead-lettered, matching contracts/messaging/topology.yaml's
/// `commitOffsetAfter`.
/// </summary>
public sealed class WorkflowConsumerWorker(
    IServiceScopeFactory scopeFactory,
    IKafkaClientFactory kafkaClientFactory,
    IOptions<KafkaSettings> settings,
    IClock clock,
    ILogger<WorkflowConsumerWorker> logger) : BackgroundService
{
    private const int MaxAttempts = 3;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var config = settings.Value;
        using var consumer = kafkaClientFactory.CreateConsumer(config.ConsumerGroup, "backoffice-workflow-worker");
        using var dlqProducer = kafkaClientFactory.CreateProducer("backoffice-dlq-publisher");
        consumer.Subscribe(config.EventsTopic);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string>? result;
                try
                {
                    result = consumer.Consume(TimeSpan.FromSeconds(1));
                }
                catch (ConsumeException exception)
                {
                    logger.LogError(exception, "Kafka consume error");
                    continue;
                }

                if (result?.Message is null)
                {
                    continue;
                }

                await ProcessMessageAsync(result.Message.Value, dlqProducer, config, stoppingToken);
                consumer.Commit(result);
            }
        }
        finally
        {
            consumer.Close();
        }
    }

    /// <summary>One envelope's worth of processing, exposed for direct testing.</summary>
    internal async Task ProcessMessageAsync(
        string rawEnvelope, IProducer<string, string> dlqProducer, KafkaSettings config, CancellationToken cancellationToken)
    {
        EventEnvelope envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<EventEnvelope>(rawEnvelope)
                ?? throw new InvalidOperationException("Envelope deserialized to null.");
        }
        catch (Exception exception)
        {
            // A malformed payload is never retryable and carries no reliable tenant/case id
            // to key off — dead-letter it directly rather than letting deserialization
            // failure escape uncaught and take down the whole consumer loop.
            logger.LogError(exception, "Unparseable event envelope on {Topic}; dead-lettering raw payload", config.EventsTopic);
            using var parseFailureScope = scopeFactory.CreateScope();
            parseFailureScope.ServiceProvider.GetRequiredService<IDeadLetterRepository>().Add(DeadLetter.Create(
                "consumer", config.EventsTopic, Guid.NewGuid(), "unknown", Guid.Empty, "Unparseable",
                rawEnvelope, exception.Message, 1, clock.UtcNow));
            await parseFailureScope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken);
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var inboxRepository = scope.ServiceProvider.GetRequiredService<IInboxRepository>();
        var auditRepository = scope.ServiceProvider.GetRequiredService<IAuditRepository>();
        var caseRepository = scope.ServiceProvider.GetRequiredService<ICaseRepository>();
        var deadLetterRepository = scope.ServiceProvider.GetRequiredService<IDeadLetterRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        if (await inboxRepository.ExistsAsync(config.ConsumerGroup, envelope.EventId, cancellationToken))
        {
            logger.LogInformation("Event {EventId} already processed by {ConsumerGroup} (duplicate)", envelope.EventId, config.ConsumerGroup);
            return;
        }

        Exception? lastError = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                await ApplyEffectAsync(envelope, caseRepository, clock, cancellationToken);
                inboxRepository.Add(InboxRecord.Create(config.ConsumerGroup, envelope.EventId, "{\"status\":\"PROCESSED\"}", clock.UtcNow));
                auditRepository.Add(AuditRecord.Create(
                    envelope.EventId, envelope.EventType, envelope.TenantId, envelope.CaseId, envelope.CorrelationId, envelope.CausationId,
                    ExtractString(envelope.Payload, "policyAction"), ExtractRuleReferences(envelope.Payload),
                    rawEnvelope, envelope.OccurredAt, clock.UtcNow));
                await unitOfWork.SaveChangesAsync(cancellationToken);
                logger.LogInformation("Event {EventId} processed", envelope.EventId);
                return;
            }
            catch (Exception exception)
            {
                lastError = exception;
                logger.LogWarning(exception, "Event {EventId} failed on attempt {Attempt}/{MaxAttempts}", envelope.EventId, attempt, MaxAttempts);
            }
        }

        deadLetterRepository.Add(DeadLetter.Create(
            "consumer", config.EventsTopic, envelope.EventId, envelope.TenantId, envelope.CaseId, envelope.EventType,
            rawEnvelope, lastError?.Message ?? "unknown", MaxAttempts, clock.UtcNow));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        try
        {
            await dlqProducer.ProduceAsync(
                config.DlqTopic, new Message<string, string> { Key = envelope.CaseId.ToString(), Value = rawEnvelope }, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "DLQ publish failed for event {EventId}; durable dead letter row remains", envelope.EventId);
        }
    }

    private static Task ApplyEffectAsync(EventEnvelope envelope, ICaseRepository caseRepository, IClock clock, CancellationToken cancellationToken) =>
        envelope.EventType == "TimerFired"
            ? ApplyTimerFiredAsync(envelope, caseRepository, clock, cancellationToken)
            : Task.CompletedTask;

    private static async Task ApplyTimerFiredAsync(EventEnvelope envelope, ICaseRepository caseRepository, IClock clock, CancellationToken cancellationToken)
    {
        var timerType = envelope.Payload.TryGetProperty("timerType", out var timerTypeElement) ? timerTypeElement.GetString() : null;
        if (timerType != "CASE_EXPIRY")
        {
            return;
        }

        var @case = await caseRepository.FindByIdAsync(envelope.TenantId, envelope.CaseId, cancellationToken);
        @case?.ExpireIfEligible(envelope.CorrelationId, clock.UtcNow);
    }

    private static string? ExtractString(JsonElement payload, string propertyName) =>
        payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static IReadOnlyList<string> ExtractRuleReferences(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object
            || !payload.TryGetProperty("ruleReferences", out var value)
            || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray().Select(e => e.GetString() ?? string.Empty).Where(s => s.Length > 0).ToList();
    }
}
