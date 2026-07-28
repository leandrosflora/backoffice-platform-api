using System.Text.Json;
using Backoffice.Application.Abstractions;
using Backoffice.Application.Audit;
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
/// otherwise see other tests' rows in a shared database.
///
/// Each worker call (DispatchOnceAsync/ProcessMessageAsync/FireDueTimersOnceAsync) opens its
/// own DbContext scope internally, matching how it runs in production. Setup and
/// verification here therefore each use a fresh, short-lived scope too — reusing one scope
/// across a worker call would let EF Core's identity map return a stale, still-tracked
/// in-memory entity instead of re-reading what the worker's own scope actually committed.
/// Policy gating for the eventing-operations HTTP surface is covered separately in
/// Backoffice.Api.Tests (real OPA).
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
            ConsumeResult<string, string>? result;
            try
            {
                result = consumer.Consume(TimeSpan.FromSeconds(2));
            }
            catch (ConsumeException)
            {
                // Auto-created topic metadata hasn't propagated to this consumer yet
                // (single-node broker, freshly started or first touch of the topic); retry.
                continue;
            }

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
        var @case = NewCase("tenant-outbox-atomic", "ext-outbox-atomic-1", testServices.Clock.UtcNow);

        using (var scope = testServices.ScopeFactory.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ICaseRepository>().Add(@case);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        using (var scope = testServices.ScopeFactory.CreateScope())
        {
            var rows = await scope.ServiceProvider.GetRequiredService<IOutboxRepository>().ListByTenantAsync("tenant-outbox-atomic", 10);
            Assert.Single(rows);
            Assert.Equal("CaseCreated", rows[0].EventType);
            Assert.Equal(OutboxStatus.Pending, rows[0].Status);
            Assert.Equal(@case.CaseId, rows[0].AggregateId);
        }
    }

    [Fact]
    public async Task OutboxDispatcher_PublishesToRealKafka_MarksPublished()
    {
        await using var testServices = await TestServices.CreateAsync(fixture.KafkaBootstrapServers);
        var @case = NewCase("tenant-outbox-dispatch", "ext-outbox-dispatch-1", testServices.Clock.UtcNow);

        using (var scope = testServices.ScopeFactory.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ICaseRepository>().Add(@case);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        IKafkaClientFactory kafkaFactory;
        KafkaSettings settings;
        using (var scope = testServices.ScopeFactory.CreateScope())
        {
            kafkaFactory = scope.ServiceProvider.GetRequiredService<IKafkaClientFactory>();
            settings = scope.ServiceProvider.GetRequiredService<IOptions<KafkaSettings>>().Value;
        }

        using var consumer = kafkaFactory.CreateConsumer($"test-consumer-{Guid.NewGuid():N}", "test-consumer");
        consumer.Subscribe(settings.EventsTopic);

        var dispatcher = new OutboxDispatcherWorker(testServices.ScopeFactory, kafkaFactory, testServices.Clock, NullLogger<OutboxDispatcherWorker>.Instance);
        using var producer = kafkaFactory.CreateProducer("test-outbox-publisher");
        var dispatched = await dispatcher.DispatchOnceAsync(producer, CancellationToken.None);
        Assert.Equal(1, dispatched);

        var consumeResult = ConsumeMatching(consumer, e => e.CaseId == @case.CaseId, TimeSpan.FromSeconds(20));
        var envelope = JsonSerializer.Deserialize<EventEnvelope>(consumeResult.Message.Value)!;
        Assert.Equal("CaseCreated", envelope.EventType);
        Assert.Equal(@case.CaseId, envelope.CaseId);

        using (var scope = testServices.ScopeFactory.CreateScope())
        {
            var rows = await scope.ServiceProvider.GetRequiredService<IOutboxRepository>().ListByTenantAsync("tenant-outbox-dispatch", 10);
            Assert.Equal(OutboxStatus.Published, rows[0].Status);
            Assert.NotNull(rows[0].PublishedAt);
        }
    }

    [Fact]
    public async Task OutboxDispatcher_StaleInFlightRow_IsReclaimedOnLaterClaim()
    {
        await using var testServices = await TestServices.CreateAsync(fixture.KafkaBootstrapServers);
        var @case = NewCase("tenant-outbox-stale", "ext-outbox-stale-1", testServices.Clock.UtcNow);

        using (var scope = testServices.ScopeFactory.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ICaseRepository>().Add(@case);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        // Simulate a dispatcher crash mid-publish: claim it (-> IN_FLIGHT) and never mark published.
        using (var scope = testServices.ScopeFactory.CreateScope())
        {
            var outboxRepository = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
            var firstClaim = await outboxRepository.ClaimAsync(10, TimeSpan.FromSeconds(120), testServices.Clock.UtcNow, CancellationToken.None);
            Assert.Single(firstClaim);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        testServices.Clock.UtcNow = testServices.Clock.UtcNow.AddMinutes(5);

        // The reclaim pass and the candidate-selection pass both run over the same
        // in-memory materialized set within one ClaimAsync call, so a row past the
        // staleness window is reclaimed and immediately re-claimable in the same cycle.
        using (var scope = testServices.ScopeFactory.CreateScope())
        {
            var outboxRepository = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
            var reclaimAttempt = await outboxRepository.ClaimAsync(10, TimeSpan.FromSeconds(1), testServices.Clock.UtcNow, CancellationToken.None);
            Assert.Single(reclaimAttempt);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }
    }

    [Fact]
    public async Task OutboxDispatcher_PublishFailsRepeatedly_MovesToDeadLetter()
    {
        await using var testServices = await TestServices.CreateAsync(fixture.KafkaBootstrapServers);
        var @case = NewCase("tenant-outbox-dlq", "ext-outbox-dlq-1", testServices.Clock.UtcNow);

        using (var scope = testServices.ScopeFactory.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ICaseRepository>().Add(@case);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        IKafkaClientFactory kafkaFactory;
        using (var scope = testServices.ScopeFactory.CreateScope())
        {
            kafkaFactory = scope.ServiceProvider.GetRequiredService<IKafkaClientFactory>();
        }

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

        using (var scope = testServices.ScopeFactory.CreateScope())
        {
            var rows = await scope.ServiceProvider.GetRequiredService<IOutboxRepository>().ListByTenantAsync("tenant-outbox-dlq", 10);
            Assert.Equal(OutboxStatus.DeadLetter, rows[0].Status);
            Assert.Equal(3, rows[0].Attempts);

            var deadLetters = await scope.ServiceProvider.GetRequiredService<IDeadLetterRepository>().ListByTenantAsync("tenant-outbox-dlq", 10);
            Assert.Single(deadLetters);
            Assert.Equal("outbox", deadLetters[0].Source);
            Assert.Equal(@case.CaseId, deadLetters[0].AggregateId);
        }
    }

    [Fact]
    public async Task WorkflowConsumer_KnownEventId_IsSkippedAsDuplicate()
    {
        await using var testServices = await TestServices.CreateAsync(fixture.KafkaBootstrapServers);

        // AwaitingDocuments -> Expired is a valid transition, so if dedup did NOT short
        // circuit, this case would visibly change state — making the guard's effect observable.
        var @case = NewCase("tenant-dedup", "ext-dedup-1", testServices.Clock.UtcNow);
        @case.Transition(
            @case.CaseVersion, CaseState.AwaitingDocuments, "DocumentsRequested", "test-actor", "case-intake",
            Guid.NewGuid(), null, "Awaiting documents.", testServices.Clock.UtcNow);

        KafkaSettings settings;
        var eventId = Guid.NewGuid();
        using (var scope = testServices.ScopeFactory.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ICaseRepository>().Add(@case);
            settings = scope.ServiceProvider.GetRequiredService<IOptions<KafkaSettings>>().Value;
            scope.ServiceProvider.GetRequiredService<IInboxRepository>()
                .Add(InboxRecord.Create(settings.ConsumerGroup, eventId, "{}", testServices.Clock.UtcNow));
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        var envelopeJson = BuildTimerFiredEnvelopeJson(eventId, "tenant-dedup", @case.CaseId, "CASE_EXPIRY", testServices.Clock.UtcNow);

        IKafkaClientFactory kafkaFactory;
        using (var scope = testServices.ScopeFactory.CreateScope())
        {
            kafkaFactory = scope.ServiceProvider.GetRequiredService<IKafkaClientFactory>();
        }

        var worker = new WorkflowConsumerWorker(
            testServices.ScopeFactory, kafkaFactory, Microsoft.Extensions.Options.Options.Create(settings), testServices.Clock, NullLogger<WorkflowConsumerWorker>.Instance);
        using var dlqProducer = kafkaFactory.CreateProducer("test-dlq-producer-dedup");
        await worker.ProcessMessageAsync(envelopeJson, dlqProducer, settings, CancellationToken.None);

        using (var scope = testServices.ScopeFactory.CreateScope())
        {
            var caseAfter = await scope.ServiceProvider.GetRequiredService<ICaseRepository>().FindByIdAsync("tenant-dedup", @case.CaseId, CancellationToken.None);
            Assert.Equal(CaseState.AwaitingDocuments, caseAfter!.State);
        }
    }

    [Fact]
    public async Task WorkflowConsumer_ProcessedEvent_IsIngestedIntoAuditStoreWithRuleReferences()
    {
        await using var testServices = await TestServices.CreateAsync(fixture.KafkaBootstrapServers);

        KafkaSettings settings;
        IKafkaClientFactory kafkaFactory;
        using (var scope = testServices.ScopeFactory.CreateScope())
        {
            settings = scope.ServiceProvider.GetRequiredService<IOptions<KafkaSettings>>().Value;
            kafkaFactory = scope.ServiceProvider.GetRequiredService<IKafkaClientFactory>();
        }

        // Shaped like the payload BackofficeDbContext's outbox hook actually generates for a
        // DecisionApproved timeline entry (spec: audit-compliance, "Decision record cites
        // its governing rule").
        var eventId = Guid.NewGuid();
        var caseId = Guid.NewGuid();
        var occurredAt = testServices.Clock.UtcNow;
        var envelopeJson = JsonSerializer.Serialize(new
        {
            eventId,
            eventType = "DecisionApproved",
            eventVersion = 1,
            occurredAt,
            tenantId = "tenant-audit-ingest",
            caseId,
            correlationId = Guid.NewGuid(),
            causationId = (Guid?)null,
            producer = "intelligent-backoffice-dotnet",
            dataClassification = "INTERNAL",
            replayCount = 0,
            replayOf = (Guid?)null,
            payload = new
            {
                caseId,
                caseVersion = 5,
                eventType = "DecisionApproved",
                actorId = "approver-1",
                origin = "approval",
                reason = "approved within authority limit",
                occurredAt,
                ruleReferences = new[] { "BR-012", "BR-013", "BR-014", "BR-015" },
                policyAction = "approval.decide",
            },
        });

        var worker = new WorkflowConsumerWorker(
            testServices.ScopeFactory, kafkaFactory, Microsoft.Extensions.Options.Options.Create(settings), testServices.Clock, NullLogger<WorkflowConsumerWorker>.Instance);
        using var dlqProducer = kafkaFactory.CreateProducer("test-dlq-producer-audit");
        await worker.ProcessMessageAsync(envelopeJson, dlqProducer, settings, CancellationToken.None);

        using (var scope = testServices.ScopeFactory.CreateScope())
        {
            var records = await scope.ServiceProvider.GetRequiredService<IAuditRepository>().ListByTenantAsync("tenant-audit-ingest", 10);
            var record = Assert.Single(records);
            Assert.Equal(eventId, record.EventId);
            Assert.Equal("DecisionApproved", record.EventType);
            Assert.Equal(caseId, record.AggregateId);
            Assert.Equal("approval.decide", record.PolicyAction);
            Assert.Equal(["BR-012", "BR-013", "BR-014", "BR-015"], record.RuleReferences);
        }
    }

    [Fact]
    public async Task EndToEnd_CaseExpiryTimerFiresThroughRealKafka_ExpiresCase()
    {
        await using var testServices = await TestServices.CreateAsync(fixture.KafkaBootstrapServers);
        var @case = NewCase("tenant-e2e-expiry", "ext-e2e-expiry-1", testServices.Clock.UtcNow);
        @case.Transition(
            @case.CaseVersion, CaseState.AwaitingDocuments, "DocumentsRequested", "test-actor", "case-intake",
            Guid.NewGuid(), null, "Awaiting documents.", testServices.Clock.UtcNow);

        using (var scope = testServices.ScopeFactory.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ICaseRepository>().Add(@case);
            var dueTimer = EventTimer.Schedule(
                "tenant-e2e-expiry", @case.CaseId, "CASE_EXPIRY", testServices.Clock.UtcNow.AddSeconds(-1), "{}", testServices.Clock.UtcNow);
            scope.ServiceProvider.GetRequiredService<ITimerRepository>().Add(dueTimer);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        // 1. Timer worker fires the due timer -> writes a TimerFired outbox row.
        var timerWorker = new TimerFiringWorker(testServices.ScopeFactory, testServices.Clock, NullLogger<TimerFiringWorker>.Instance);
        Assert.Equal(1, await timerWorker.FireDueTimersOnceAsync(CancellationToken.None));

        IKafkaClientFactory kafkaFactory;
        KafkaSettings settings;
        using (var scope = testServices.ScopeFactory.CreateScope())
        {
            kafkaFactory = scope.ServiceProvider.GetRequiredService<IKafkaClientFactory>();
            settings = scope.ServiceProvider.GetRequiredService<IOptions<KafkaSettings>>().Value;
        }

        // 2. Outbox dispatcher publishes it to the real Kafka broker.
        using var consumer = kafkaFactory.CreateConsumer($"test-consumer-{Guid.NewGuid():N}", "test-consumer");
        consumer.Subscribe(settings.EventsTopic);

        var dispatcher = new OutboxDispatcherWorker(testServices.ScopeFactory, kafkaFactory, testServices.Clock, NullLogger<OutboxDispatcherWorker>.Instance);
        using var producer = kafkaFactory.CreateProducer("test-outbox-publisher-e2e");
        // Also dispatches the CaseCreated/DocumentsRequested rows the setup above generated
        // via the same transactional-outbox hook — only the TimerFired one matters here.
        Assert.True(await dispatcher.DispatchOnceAsync(producer, CancellationToken.None) >= 1);

        // 3. Consume the real message from Kafka and feed it to the workflow worker. The
        // batch also contains this case's CaseCreated/DocumentsRequested events (same
        // CaseId), so match on eventType too to pick out the TimerFired one specifically.
        var consumeResult = ConsumeMatching(consumer, e => e.CaseId == @case.CaseId && e.EventType == "TimerFired", TimeSpan.FromSeconds(20));

        var workflowWorker = new WorkflowConsumerWorker(
            testServices.ScopeFactory, kafkaFactory, Microsoft.Extensions.Options.Options.Create(settings), testServices.Clock, NullLogger<WorkflowConsumerWorker>.Instance);
        using var dlqProducer = kafkaFactory.CreateProducer("test-dlq-producer-e2e");
        await workflowWorker.ProcessMessageAsync(consumeResult.Message.Value, dlqProducer, settings, CancellationToken.None);

        using (var scope = testServices.ScopeFactory.CreateScope())
        {
            var caseAfter = await scope.ServiceProvider.GetRequiredService<ICaseRepository>().FindByIdAsync("tenant-e2e-expiry", @case.CaseId, CancellationToken.None);
            Assert.Equal(CaseState.Expired, caseAfter!.State);
        }
    }

    [Fact]
    public async Task DeadLetterReplay_PreservesOriginalPayload_UnderNewEventId()
    {
        await using var testServices = await TestServices.CreateAsync(fixture.KafkaBootstrapServers);

        var caseId = Guid.NewGuid();
        var originalEventId = Guid.NewGuid();
        var envelopeJson = BuildTimerFiredEnvelopeJson(originalEventId, "tenant-replay", caseId, "CASE_EXPIRY", testServices.Clock.UtcNow);

        long deadLetterId;
        using (var scope = testServices.ScopeFactory.CreateScope())
        {
            var deadLetter = DeadLetter.Create(
                "consumer", "backoffice.events.v1", originalEventId, "tenant-replay", caseId, "TimerFired",
                envelopeJson, "simulated processing failure", 3, testServices.Clock.UtcNow);
            scope.ServiceProvider.GetRequiredService<IDeadLetterRepository>().Add(deadLetter);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
            deadLetterId = deadLetter.Id;
        }

        Guid replayEventId;
        using (var scope = testServices.ScopeFactory.CreateScope())
        {
            var deadLetterRepository = scope.ServiceProvider.GetRequiredService<IDeadLetterRepository>();
            var deadLetter = (await deadLetterRepository.FindByIdAsync(deadLetterId, CancellationToken.None))!;

            // Mirrors ReplayDeadLetterHandler's domain-level mechanics directly (that handler
            // is policy-gated via real OPA, already covered in Backoffice.Api.Tests) — this
            // test isolates the actual content-preservation guarantee.
            var originalEnvelope = JsonDocument.Parse(deadLetter.EnvelopeJson);
            var payloadJson = originalEnvelope.RootElement.GetProperty("payload").GetRawText();

            var replay = OutboxMessage.CreateReplay(
                deadLetter.AggregateId, deadLetter.TenantId, deadLetter.EventType, deadLetter.SourceTopic,
                deadLetter.AggregateId.ToString(), Guid.NewGuid(), deadLetter.EventId, replayCount: 1, payloadJson, testServices.Clock.UtcNow);
            scope.ServiceProvider.GetRequiredService<IOutboxRepository>().Add(replay);
            deadLetter.MarkReplayed(replay.EventId, "operator-confirmed replay after fix", testServices.Clock.UtcNow);
            scope.ServiceProvider.GetRequiredService<IReplayAuditRepository>().Add(ReplayAuditEntry.Create(
                deadLetter.Id, deadLetter.EventId, replay.EventId, "tenant-replay", "operator-1",
                "operator-confirmed replay after fix", Guid.NewGuid(), testServices.Clock.UtcNow));
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();

            Assert.NotEqual(originalEventId, replay.EventId);
            Assert.Equal(originalEventId, replay.ReplayOf);

            var originalPayload = originalEnvelope.RootElement.GetProperty("payload");
            var replayedPayload = JsonDocument.Parse(replay.PayloadJson).RootElement;
            Assert.Equal(originalPayload.GetRawText(), replayedPayload.GetRawText());

            replayEventId = replay.EventId;
        }

        using (var scope = testServices.ScopeFactory.CreateScope())
        {
            var deadLetterAfter = await scope.ServiceProvider.GetRequiredService<IDeadLetterRepository>().FindByIdAsync(deadLetterId, CancellationToken.None);
            Assert.Equal(DeadLetterStatus.Replayed, deadLetterAfter!.Status);
            Assert.Equal(replayEventId, deadLetterAfter.ReplayEventId);
        }
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
