using Backoffice.Application.Eventing;
using Backoffice.Domain.Eventing;
using Backoffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Backoffice.Infrastructure.Eventing;

public sealed class OutboxRepository(BackofficeDbContext dbContext) : IOutboxRepository
{
    public void Add(OutboxMessage message) => dbContext.Outbox.Add(message);

    public async Task<IReadOnlyList<OutboxMessage>> ClaimAsync(
        int limit, TimeSpan staleness, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var staleInFlight = await dbContext.Outbox
            .Where(m => m.Status == OutboxStatus.InFlight)
            .ToListAsync(cancellationToken);
        foreach (var message in staleInFlight)
        {
            message.ReclaimIfStale(staleness, now);
        }

        var candidates = await dbContext.Outbox
            .Where(m => (m.Status == OutboxStatus.Pending || m.Status == OutboxStatus.Retry) && m.AvailableAt <= now)
            .OrderBy(m => m.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

        foreach (var message in candidates)
        {
            message.MarkInFlight(now);
        }

        return candidates;
    }

    public async Task<IReadOnlyList<OutboxMessage>> ListByTenantAsync(string tenantId, int limit, CancellationToken cancellationToken = default)
    {
        var messages = await dbContext.Outbox
            .Where(m => m.TenantId == tenantId)
            .ToListAsync(cancellationToken);

        return messages.OrderByDescending(m => m.Id).Take(limit).ToList();
    }
}

public sealed class InboxRepository(BackofficeDbContext dbContext) : IInboxRepository
{
    public Task<bool> ExistsAsync(string consumerName, Guid eventId, CancellationToken cancellationToken = default) =>
        dbContext.Inbox.AnyAsync(r => r.ConsumerName == consumerName && r.EventId == eventId, cancellationToken);

    public void Add(InboxRecord record) => dbContext.Inbox.Add(record);
}

public sealed class TimerRepository(BackofficeDbContext dbContext) : ITimerRepository
{
    public void Add(EventTimer timer) => dbContext.Timers.Add(timer);

    public async Task<IReadOnlyList<EventTimer>> ClaimDueAsync(
        int limit, TimeSpan staleness, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var staleInFlight = await dbContext.Timers
            .Where(t => t.Status == TimerStatus.InFlight)
            .ToListAsync(cancellationToken);
        foreach (var timer in staleInFlight)
        {
            timer.ReclaimIfStale(staleness, now);
        }

        var candidates = await dbContext.Timers
            .Where(t => (t.Status == TimerStatus.Scheduled || t.Status == TimerStatus.Retry) && t.DueAt <= now)
            .OrderBy(t => t.DueAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

        foreach (var timer in candidates)
        {
            timer.MarkInFlight(now);
        }

        return candidates;
    }

    public async Task<IReadOnlyList<EventTimer>> ListByTenantAsync(string tenantId, int limit, CancellationToken cancellationToken = default)
    {
        var timers = await dbContext.Timers
            .Where(t => t.TenantId == tenantId)
            .ToListAsync(cancellationToken);

        return timers.OrderByDescending(t => t.CreatedAt).Take(limit).ToList();
    }
}

public sealed class DeadLetterRepository(BackofficeDbContext dbContext) : IDeadLetterRepository
{
    public void Add(DeadLetter deadLetter) => dbContext.DeadLetters.Add(deadLetter);

    public Task<DeadLetter?> FindByIdAsync(long id, CancellationToken cancellationToken = default) =>
        dbContext.DeadLetters.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    public async Task<IReadOnlyList<DeadLetter>> ListByTenantAsync(string tenantId, int limit, CancellationToken cancellationToken = default)
    {
        var deadLetters = await dbContext.DeadLetters
            .Where(d => d.TenantId == tenantId)
            .ToListAsync(cancellationToken);

        return deadLetters.OrderByDescending(d => d.Id).Take(limit).ToList();
    }
}

public sealed class ReplayAuditRepository(BackofficeDbContext dbContext) : IReplayAuditRepository
{
    public void Add(ReplayAuditEntry entry) => dbContext.ReplayAudits.Add(entry);
}
