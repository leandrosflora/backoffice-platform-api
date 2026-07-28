namespace Backoffice.Application.Abstractions;

/// <summary>
/// Single save-changes entry point for every command handler, so an aggregate
/// mutation and its outbox/timeline side effects are always committed atomically
/// in one transaction (design.md: "single IUnitOfWork.SaveChangesAsync() call per
/// command handler").
/// </summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
