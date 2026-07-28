using System.Text.Json;
using Backoffice.Application.Abstractions;
using Backoffice.Application.Cases;
using Backoffice.Application.Eventing;
using Backoffice.Domain.Cases;
using Backoffice.Domain.Common;
using Backoffice.Domain.Eventing;
using Backoffice.Infrastructure.Eventing;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Backoffice.Workers.Tests;

/// <summary>
/// Exercises the outbox/inbox/retry/DLQ/timer mechanics against a real SQLite-backed
/// DbContext and a real single-node Kafka broker (spec: eventing-reliability). Each test
/// gets its own isolated <see cref="TestServices"/> (fresh DB) sharing only the expensive
/// Kafka container, since <c>ClaimAsync</c> is deliberately tenant-agnostic and would
/// otherwise see other tests' rows in a shared database. Policy gating for the
/// eventing-operations HTTP surface is covered separately in Backoffice.Api.Tests (real OPA).
/// </summary>
public class EventingTests(WorkersTestFixture fixture) : IClassFixture<WorkersTestFixture>
{
    private static Case NewCase(string tenantId, string externalRef, DateTimeOffset now) =>
        Case.Create(tenantId, externalRef, DisputeType.CardPurchase, Channel.App, Priority.Normal,
            new Money("BRL", 150.00m), Guid.NewGuid(), "test-actor", now);

    private static ConsumeResult<string, string> ConsumeMatching(
        IConsumer<string, string> consumer, Func<EventEnvelope, bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            var result = consumer.Consume(TimeSpan.FromSeconds(2));
            if (result?.Message is null)
            {
                continue;
            }

            var envelope = JsonSerializer.Deserialize<EventEnvelope>(result.Message.Value);
            if (envelope is not null && predicate(envelope))
            {
                return result;
            }
        }

        throw new TimeoutException("Expected Kafka message was not received within the timeout.");
    }

    [Fact]
    public async Task CreatingCase_WritesOutboxRowInSameSaveChanges()
    {
        await using var testServices = await TestServices.CreateAsync(fixture.KafkaBootstrapServers);
        using var scope = testServices.ScopeFactory.CreateScope();
        var caseRepository = scope.ServiceProvider.GetRequiredService<ICaseRepository>();
        var outboxRepository = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var @case = NewCase("tenant-outbox-atomic", "ext-outbox-atomic-1", testServices.Clock.UtcNow);
        caseRepository.Add(@case);
        await unitOfWork.SaveChangesAsync();

        var rows = await outboxRepository.ListByTenantAsync("tenant-outbox-atomic", 10);
        Assert.Single(rows);
        Assert.Equal("CaseCreated", rows[0].EventType);
        Assert.Equal(OutboxStatus.Pending, rows[0].Status);
        Assert.Equal(@case.CaseId, rows[0].AggregateId);
    }

    [Fact]
    public async Task OutboxDispatcher_PublishesToRealKafka_MarksPublished()
    {
        await using var testServices = await TestServices.CreateAsync(fixture.KafkaBootstrapServers);
        using var scope = testServices.ScopeFactory.CreateScope();
        var caseRepository = scope.ServiceProvider.GetRequiredService<ICaseRepository>();
        var outboxRepository = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var kafkaFactory = scope.ServiceProvider.GetRequiredService<IKafkaClientFactory>();
        var settings = scope.ServiceProvider.GetRequiredService<IOptions<KafkaSettings>>();

        var @case = NewCase("tenant-outbox-dispatch", "ext-outbox-dispatch-1", testServices.Clock.UtcNow);
        caseRepository.Add(@case);
        await unitOfWork.SaveChangesAsync();

        using var consumer = kafkaFactory.CreateConsumer($"test-consumer-{Guid.NewGuid():N}", "test-consumer");
        consumer.Subscribe(settings.Value.EventsTopic);

        var dispatcher = new OutboxDispatcherWorker(testServices.ScopeFactory, kafkaFactory, testServices.Clock, NullLogger<OutboxDispatcherWorker>.Instance);
        using var producer = kafkaFactory.CreateProducer("test-outbox-publisher");
        var dispatched = await dispatcher.DispatchOnceAsync(producer, CancellationToken.None);
        Assert.Equal(1, dispatched);

        var consumeResult = ConsumeMatching(consumer, e => e.CaseId == @case.CaseId, TimeSpan.FromSeconds(20));
        var envelope = JsonSerializer.Deserialize<EventEnvelope>(consumeResult.Message.Value)!;
        Assert.Equal("CaseCreated", envelope.EventType);
        Assert.Equal(@case.CaseId, envelope.CaseId);

        var rows = await outboxRepository.ListByTenantAsync("tenant-outbox-dispatch", 10);
        Assert.Equal(OutboxStatus.Published, rows[0].Status);
        Assert.NotNull(rows[0].PublishedAt);
    }

    [Fact]
    public async Task OutboxDispatcher_StaleInFlightRow_IsReclaimedOnLaterClaim()
    {
        await using var testServices = await TestServices.CreateAsync(fixture.KafkaBootstrapServers);
        using var scope = testServices.ScopeFactory.CreateScope();
        var caseRepository = scope.ServiceProvider.GetRequiredService<ICaseRepository>();
        var outboxRepository = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var @case = NewCase("tenant-outbox-stale", "ext-outbox-stale-1", testServices.Clock.UtcNow);
        caseRepository.Add(@case);
        await unitOfWork.SaveChangesAsync();

        // Simulate a dispatcher crash mid-publish: claim it (-> IN_FLIGHT) and never mark published.
        var firstClaim = await outboxRepository.ClaimAsync(10, TimeSpan.FromSeconds(120), testServices.Clock.UtcNow, CancellationToken.None);
        Assert.Single(firstClaim);
        await unitOfWork.SaveChangesAsync();

        testServices.Clock.UtcNow = testServices.Clock.UtcNow.AddMinutes(5);

        // Within a single ClaimAsync call the stale-reclaim pass and the candidate-selection
        // pass run against the same not-yet-saved change tracker state, so the row isn't
        // eligible again until the reclaim is persisted and a subsequent claim runs.
        var sameCycleAttempt = await outboxRepository.ClaimAsync(10, TimeSpan.FromSeconds(1), testServices.Clock.UtcNow, CancellationToken.None);
        Assert.Empty(sameCycleAttempt);
        await unitOfWork.SaveChangesAsync();

        var nextCycleAttempt = await outboxRepository.ClaimAsync(10, TimeSpan.FromSeconds(1), testServices.Clock.UtcNow, CancellationToken.None);
        Assert.Single(nextCycleAttempt);
    }

    [Fact]
    public async Task OutboxDispatcher_PublishFailsRepeatedly_MovesToDeadLetter()
    {
        await using var testServices = await TestServices.CreateAsync(fixture.KafkaBootstrapServers);
        using var scope = testServices.ScopeFactory.CreateScope();
        var caseRepository = scope.ServiceProvider.GetRequiredService<ICaseRepository>();
        var outboxRepository = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
        var deadLetterRepository = scope.ServiceProvider.GetRequiredService<IDeadLetterRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var kafkaFactory = scope.ServiceProvider.GetRequiredService<IKafkaClientFactory>();

        var @case = NewCase("tenant-outbox-dlq", "ext-outbox-dlq-1", testServices.Clock.UtcNow);
        caseRepository.Add(@case);
        await unitOfWork.SaveChangesAsync();

        // A producer pointed at an address nothing is listening on, so every publish
        // attempt fails deterministically and quickly.
        using var brokenProducer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = "127.0.0.1:1",
            MessageTimeoutMs = 1000,
            SocketConnectionSetupTimeoutMs = 1000,
        }).Build();

        var dispatcher = new OutboxDispatcherWorker(testServices.ScopeFactory, kafkaFactory, testServices.Clock, NullLogger<OutboxDispatcherWorker>.Instance);

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await dispatcher.DispatchOnceAsync(brokenProducer, CancellationToken.None);
            testServices.Clock.UtcNow = testServices.Clock.UtcNow.AddMinutes(2); // clears the exponential-backoff AvailableAt gate
        }

        var rows = await outboxRepository.ListByTenantAsync("tenant-outbox-dlq", 10);
        Assert.Equal(OutboxStatus.DeadLetter, rows[0].Status);
        Assert.Equal(3, rows[0].Attempts);

        var deadLetters = await deadLetterRepository.ListByTenantAsync("tenant-outbox-dlq", 10);
        Assert.Single(deadLetters);
        Assert.Equal("outbox", deadLetters[0].Source);
        Assert.Equal(@case.CaseId, deadLetters[0].AggregateId);
    }

    [Fact]
    public async Task WorkflowConsumer_KnownEventId_IsSkippedAsDuplicate()
    {
        await using var testServices = await TestServices.CreateAsync(fixture.KafkaBootstrapServers);
        using var scope = testServices.ScopeFactory.CreateScope();
        var caseRepository = scope.ServiceProvider.GetRequiredService<ICaseRepository>();
        var inboxRepository = scope.ServiceProvider.GetRequiredService<IInboxRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var kafkaFactory = scope.ServiceProvider.GetRequiredService<IKafkaClientFactory>();
        var settings = scope.ServiceProvider.GetRequiredService<IOptions<KafkaSettings>>();

        // AwaitingDocuments -> Expired is a valid transition, so if dedup did NOT short
        // circuit, this case would visibly change state — making the guard's effect observable.
        var @case = NewCase("tenant-dedup", "ext-dedup-1", testServices.Clock.UtcNow);
        @case.Transition(
            @case.CaseVersion, CaseState.AwaitingDocuments, "DocumentsRequested", "test-actor", "case-intake",
            Guid.NewGuid(), null, "Awaiting documents.", testServices.Clock.UtcNow);
        caseRepository.Add(@case);
        await unitOfWork.SaveChangesAsync();

        var eventId = Guid.NewGuid();
        inboxRepository.Add(InboxRecord.Create(settings.Value.ConsumerGroup, eventId, "{}", testServices.Clock.UtcNow));
        await unitOfWork.SaveChangesAsync();

        var envelopeJson = BuildTimerFiredEnvelopeJson(eventId, "tenant-dedup", @case.CaseId, "CASE_EXPIRY", testServices.Clock.UtcNow);

        var worker = new WorkflowConsumerWorker(testServices.ScopeFactory, kafkaFactory, settings, testServices.Clock, NullLogger<WorkflowConsumerWorker>.Instance);
        using var dlqProducer = kafkaFactory.CreateProducer("test-dlq-producer-dedup");
        await worker.ProcessMessageAsync(envelopeJson, dlqProducer, settings.Value, CancellationToken.None);

        var caseAfter = await caseRepository.FindByIdAsync("tenant-dedup", @case.CaseId, CancellationToken.None);
        Assert.Equal(CaseState.AwaitingDocuments, caseAfter!.State);
    }

    [Fact]
    public async Task EndToEnd_CaseExpiryTimerFiresThroughRealKafka_ExpiresCase()
    {
        await using var testServices = await TestServices.CreateAsync(fixture.KafkaBootstrapServers);
        using var scope = testServices.ScopeFactory.CreateScope();
        var caseRepository = scope.ServiceProvider.GetRequiredService<ICaseRepository>();
        var timerRepository = scope.ServiceProvider.GetRequiredService<ITimerRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var kafkaFactory = scope.ServiceProvider.GetRequiredService<IKafkaClientFactory>();
        var settings = scope.ServiceProvider.GetRequiredService<IOptions<KafkaSettings>>();

        var @case = NewCase("tenant-e2e-expiry", "ext-e2e-expiry-1", testServices.Clock.UtcNow);
        @case.Transition(
            @case.CaseVersion, CaseState.AwaitingDocuments, "DocumentsRequested", "test-actor", "case-intake",
            Guid.NewGuid(), null, "Awaiting documents.", testServices.Clock.UtcNow);
        caseRepository.Add(@case);

        var dueTimer = EventTimer.Schedule(
            "tenant-e2e-expiry", @case.CaseId, "CASE_EXPIRY", testServices.Clock.UtcNow.AddSeconds(-1), "{}", testServices.Clock.UtcNow);
        timerRepository.Add(dueTimer);
        await unitOfWork.SaveChangesAsync();

        // 1. Timer worker fires the due timer -> writes a TimerFired outbox row.
        var timerWorker = new TimerFiringWorker(testServices.ScopeFactory, testServices.Clock, NullLogger<TimerFiringWorker>.Instance);
        Assert.Equal(1, await timerWorker.FireDueTimersOnceAsync(CancellationToken.None));

        // 2. Outbox dispatcher publishes it to the real Kafka broker.
        using var consumer = kafkaFactory.CreateConsumer($"test-consumer-{Guid.NewGuid():N}", "test-consumer");
        consumer.Subscribe(settings.Value.EventsTopic);

        var dispatcher = new OutboxDispatcherWorker(testServices.ScopeFactory, kafkaFactory, testServices.Clock, NullLogger<OutboxDispatcherWorker>.Instance);
        using var producer = kafkaFactory.CreateProducer("test-outbox-publisher-e2e");
        Assert.Equal(1, await dispatcher.DispatchOnceAsync(producer, CancellationToken.None));

        // 3. Consume the real message from Kafka and feed it to the workflow worker.
        var consumeResult = ConsumeMatching(consumer, e => e.CaseId == @case.CaseId, TimeSpan.FromSeconds(20));

        var workflowWorker = new WorkflowConsumerWorker(testServices.ScopeFactory, kafkaFactory, settings, testServices.Clock, NullLogger<WorkflowConsumerWorker>.Instance);
        using var dlqProducer = kafkaFactory.CreateProducer("test-dlq-producer-e2e");
        await workflowWorker.ProcessMessageAsync(consumeResult.Message.Value, dlqProducer, settings.Value, CancellationToken.None);

        var caseAfter = await caseRepository.FindByIdAsync("tenant-e2e-expiry", @case.CaseId, CancellationToken.None);
        Assert.Equal(CaseState.Expired, caseAfter!.State);
    }

    [Fact]
    public async Task DeadLetterReplay_PreservesOriginalPayload_UnderNewEventId()
    {
        await using var testServices = await TestServices.CreateAsync(fixture.KafkaBootstrapServers);
        using var scope = testServices.ScopeFactory.CreateScope();
        var deadLetterRepository = scope.ServiceProvider.GetRequiredService<IDeadLetterRepository>();
        var outboxRepository = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
        var replayAuditRepository = scope.ServiceProvider.GetRequiredService<IReplayAuditRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var caseId = Guid.NewGuid();
        var originalEventId = Guid.NewGuid();
        var envelopeJson = BuildTimerFiredEnvelopeJson(originalEventId, "tenant-replay", caseId, "CASE_EXPIRY", testServices.Clock.UtcNow);

        var deadLetter = DeadLetter.Create(
            "consumer", "backoffice.events.v1", originalEventId, "tenant-replay", caseId, "TimerFired",
            envelopeJson, "simulated processing failure", 3, testServices.Clock.UtcNow);
        deadLetterRepository.Add(deadLetter);
        await unitOfWork.SaveChangesAsync();

        // Mirrors ReplayDeadLetterHandler's domain-level mechanics directly (that handler is
        // policy-gated via real OPA, already covered in Backoffice.Api.Tests) — this test
        // isolates the actual content-preservation guarantee.
        var originalEnvelope = JsonDocument.Parse(deadLetter.EnvelopeJson);
        var payloadJson = originalEnvelope.RootElement.GetProperty("payload").GetRawText();

        var replay = OutboxMessage.CreateReplay(
            deadLetter.AggregateId, deadLetter.TenantId, deadLetter.EventType, deadLetter.SourceTopic,
            deadLetter.AggregateId.ToString(), Guid.NewGuid(), deadLetter.EventId, replayCount: 1, payloadJson, testServices.Clock.UtcNow);
        outboxRepository.Add(replay);
        deadLetter.MarkReplayed(replay.EventId, "operator-confirmed replay after fix", testServices.Clock.UtcNow);
        replayAuditRepository.Add(ReplayAuditEntry.Create(
            deadLetter.Id, deadLetter.EventId, replay.EventId, "tenant-replay", "operator-1",
            "operator-confirmed replay after fix", Guid.NewGuid(), testServices.Clock.UtcNow));
        await unitOfWork.SaveChangesAsync();

        Assert.NotEqual(originalEventId, replay.EventId);
        Assert.Equal(originalEventId, replay.ReplayOf);

        var originalPayload = originalEnvelope.RootElement.GetProperty("payload");
        var replayedPayload = JsonDocument.Parse(replay.PayloadJson).RootElement;
        Assert.Equal(originalPayload.GetRawText(), replayedPayload.GetRawText());

        var deadLetterAfter = await deadLetterRepository.FindByIdAsync(deadLetter.Id, CancellationToken.None);
        Assert.Equal(DeadLetterStatus.Replayed, deadLetterAfter!.Status);
        Assert.Equal(replay.EventId, deadLetterAfter.ReplayEventId);
    }

    private static string BuildTimerFiredEnvelopeJson(Guid eventId, string tenantId, Guid caseId, string timerType, DateTimeOffset now) =>
        JsonSerializer.Serialize(new
        {
            eventId,
            eventType = "TimerFired",
            eventVersion = 1,
            occurredAt = now,
            tenantId,
            caseId,
            correlationId = Guid.NewGuid(),
            causationId = (Guid?)null,
            producer = "timer-worker",
            dataClassification = "INTERNAL",
            replayCount = 0,
            replayOf = (Guid?)null,
            payload = new { timerId = Guid.NewGuid(), timerType, payload = new { } },
        });
}
