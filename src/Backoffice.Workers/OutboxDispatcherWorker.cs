using System.Text.Json;
using Backoffice.Application.Abstractions;
using Backoffice.Application.Eventing;
using Backoffice.Domain.Eventing;
using Backoffice.Infrastructure.Eventing;
using Confluent.Kafka;

namespace Backoffice.Workers;

/// <summary>
/// Claims PENDING/RETRY outbox rows and publishes them to Kafka, marking PUBLISHED on
/// success or RETRY/DEAD_LETTER on failure (spec: eventing-reliability, "Outbox dispatch to
/// the event backbone").
/// </summary>
public sealed class OutboxDispatcherWorker(
    IServiceScopeFactory scopeFactory,
    IKafkaClientFactory kafkaClientFactory,
    IClock clock,
    ILogger<OutboxDispatcherWorker> logger) : BackgroundService
{
    // Matches contracts/messaging/topology.yaml's outboxPublisher.claimTimeoutSeconds.
    private static readonly TimeSpan ClaimTimeout = TimeSpan.FromSeconds(120);
    private const int MaxAttempts = 3;
    private const int BatchLimit = 20;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var producer = kafkaClientFactory.CreateProducer("backoffice-outbox-publisher");

        while (!stoppingToken.IsCancellationRequested)
        {
            var dispatched = await DispatchOnceAsync(producer, stoppingToken);
            if (dispatched == 0)
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
        }
    }

    /// <summary>One claim-and-publish cycle, exposed for direct testing without running the
    /// infinite polling loop.</summary>
    internal async Task<int> DispatchOnceAsync(IProducer<string, string> producer, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var outboxRepository = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
        var deadLetterRepository = scope.ServiceProvider.GetRequiredService<IDeadLetterRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var claimed = await outboxRepository.ClaimAsync(BatchLimit, ClaimTimeout, clock.UtcNow, cancellationToken);
        if (claimed.Count == 0)
        {
            return 0;
        }

        // Persist the claim (PENDING/RETRY -> IN_FLIGHT) before attempting any publish, so a
        // crash mid-batch leaves rows correctly IN_FLIGHT for later reclaim rather than
        // ambiguous (spec: eventing-reliability, "Stale in-flight row is reclaimed").
        await unitOfWork.SaveChangesAsync(cancellationToken);

        foreach (var message in claimed)
        {
            try
            {
                var envelope = EventEnvelope.FromOutboxMessage(message);
                var json = JsonSerializer.Serialize(envelope);
                await producer.ProduceAsync(
                    message.Topic, new Message<string, string> { Key = message.MessageKey, Value = json }, cancellationToken);
                message.MarkPublished(clock.UtcNow);
                logger.LogInformation("Published event {EventId} ({EventType})", message.EventId, message.EventType);
            }
            catch (Exception exception)
            {
                var status = message.Fail(exception.Message, MaxAttempts, clock.UtcNow);
                logger.LogWarning(exception, "Outbox event {EventId} moved to {Status}", message.EventId, status);

                if (status == nameof(OutboxStatus.DeadLetter))
                {
                    var envelope = EventEnvelope.FromOutboxMessage(message);
                    deadLetterRepository.Add(DeadLetter.Create(
                        "outbox", message.Topic, message.EventId, message.TenantId, message.AggregateId, message.EventType,
                        JsonSerializer.Serialize(envelope), exception.Message, message.Attempts, clock.UtcNow));
                }
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return claimed.Count;
    }
}
