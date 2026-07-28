using Backoffice.Domain.Eventing;

namespace Backoffice.Application.Eventing;

public interface IOutboxRepository
{
    void Add(OutboxMessage message);

    /// <summary>
    /// Reclaims rows stuck IN_FLIGHT past <paramref name="staleness"/> back to RETRY, then
    /// marks up to <paramref name="limit"/> PENDING/RETRY-and-due rows IN_FLIGHT and returns
    /// them. The caller must persist via <c>IUnitOfWork.SaveChangesAsync</c> immediately
    /// after claiming — before attempting to publish — so a crash mid-publish leaves the
    /// claim durable rather than losing track of the row (spec: eventing-reliability,
    /// "Stale in-flight row is reclaimed").
    /// </summary>
    Task<IReadOnlyList<OutboxMessage>> ClaimAsync(int limit, TimeSpan staleness, DateTimeOffset now, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OutboxMessage>> ListByTenantAsync(string tenantId, int limit, CancellationToken cancellationToken = default);
}

public interface IInboxRepository
{
    Task<bool> ExistsAsync(string consumerName, Guid eventId, CancellationToken cancellationToken = default);

    void Add(InboxRecord record);
}

public interface ITimerRepository
{
    void Add(EventTimer timer);

    /// <summary>Same claim contract as <see cref="IOutboxRepository.ClaimAsync"/>, applied to due timers.</summary>
    Task<IReadOnlyList<EventTimer>> ClaimDueAsync(int limit, TimeSpan staleness, DateTimeOffset now, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EventTimer>> ListByTenantAsync(string tenantId, int limit, CancellationToken cancellationToken = default);
}

public interface IDeadLetterRepository
{
    void Add(DeadLetter deadLetter);

    Task<DeadLetter?> FindByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DeadLetter>> ListByTenantAsync(string tenantId, int limit, CancellationToken cancellationToken = default);
}

public interface IReplayAuditRepository
{
    void Add(ReplayAuditEntry entry);
}
