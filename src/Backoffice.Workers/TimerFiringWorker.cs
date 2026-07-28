using System.Text.Json;
using Backoffice.Application.Abstractions;
using Backoffice.Application.Eventing;
using Backoffice.Domain.Eventing;

namespace Backoffice.Workers;

/// <summary>
/// Claims due timers and fires them: each firing writes a `TimerFired` outbox event (picked
/// up by the workflow worker to apply the actual effect, e.g. `CASE_EXPIRY` -> EXPIRED) —
/// never mutates the aggregate directly, since the timer worker and workflow worker are
/// independently deployable processes in the distributed profile (design.md). Retries up to
/// 3 attempts with backoff before dead-lettering (spec: eventing-reliability, "Timer
/// scheduling and firing").
/// </summary>
public sealed class TimerFiringWorker(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    ILogger<TimerFiringWorker> logger) : BackgroundService
{
    // Matches contracts/messaging/topology.yaml's outboxPublisher.claimTimeoutSeconds,
    // reused here for the timer table's own IN_FLIGHT staleness window.
    private static readonly TimeSpan ClaimTimeout = TimeSpan.FromSeconds(120);
    private const int MaxAttempts = 3;
    private const int BatchLimit = 20;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    private const string EventsTopic = "backoffice.events.v1";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var fired = await FireDueTimersOnceAsync(stoppingToken);
            if (fired == 0)
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
        }
    }

    /// <summary>One claim-and-fire cycle, exposed for direct testing.</summary>
    internal async Task<int> FireDueTimersOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var timerRepository = scope.ServiceProvider.GetRequiredService<ITimerRepository>();
        var outboxRepository = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
        var deadLetterRepository = scope.ServiceProvider.GetRequiredService<IDeadLetterRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var claimed = await timerRepository.ClaimDueAsync(BatchLimit, ClaimTimeout, clock.UtcNow, cancellationToken);
        if (claimed.Count == 0)
        {
            return 0;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        foreach (var timer in claimed)
        {
            try
            {
                timer.MarkFired(clock.UtcNow);

                var payload = JsonSerializer.Serialize(new
                {
                    timerId = timer.TimerId,
                    timerType = timer.TimerType,
                    payload = JsonDocument.Parse(timer.PayloadJson).RootElement,
                });
                outboxRepository.Add(OutboxMessage.Create(
                    timer.AggregateId, timer.TenantId, "TimerFired", EventsTopic, timer.AggregateId.ToString(),
                    Guid.NewGuid(), null, "timer-worker", payload, clock.UtcNow));

                logger.LogInformation("Timer {TimerId} ({TimerType}) fired", timer.TimerId, timer.TimerType);
            }
            catch (Exception exception)
            {
                var status = timer.Fail(exception.Message, MaxAttempts, clock.UtcNow);
                logger.LogWarning(exception, "Timer {TimerId} moved to {Status}", timer.TimerId, status);

                if (status == nameof(TimerStatus.DeadLetter))
                {
                    var eventId = Guid.NewGuid();
                    var envelopeJson = JsonSerializer.Serialize(new
                    {
                        eventId,
                        eventType = "TimerFailed",
                        tenantId = timer.TenantId,
                        caseId = timer.AggregateId,
                        payload = new { timerId = timer.TimerId, timerType = timer.TimerType },
                    });
                    deadLetterRepository.Add(DeadLetter.Create(
                        "timer", EventsTopic, eventId, timer.TenantId, timer.AggregateId, "TimerFailed",
                        envelopeJson, exception.Message, timer.Attempts, clock.UtcNow));
                }
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return claimed.Count;
    }
}
