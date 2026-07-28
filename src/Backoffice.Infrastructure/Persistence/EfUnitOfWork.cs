using Backoffice.Application.Abstractions;

namespace Backoffice.Infrastructure.Persistence;

public sealed class EfUnitOfWork(BackofficeDbContext dbContext) : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
