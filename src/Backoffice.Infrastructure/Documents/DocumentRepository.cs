using Backoffice.Application.Documents;
using Backoffice.Domain.Documents;
using Backoffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Backoffice.Infrastructure.Documents;

public sealed class DocumentRepository(BackofficeDbContext dbContext) : IDocumentRepository
{
    public Task<Document?> FindByIdAsync(string tenantId, Guid caseId, Guid documentId, CancellationToken cancellationToken = default) =>
        dbContext.Documents.FirstOrDefaultAsync(
            d => d.TenantId == tenantId && d.CaseId == caseId && d.DocumentId == documentId, cancellationToken);

    public async Task<IReadOnlyList<Document>> ListByCaseAsync(string tenantId, Guid caseId, CancellationToken cancellationToken = default)
    {
        // Ordered client-side: SQLite (test/dev provider) cannot translate ORDER BY over
        // DateTimeOffset, and per-case document counts are small enough that this is fine.
        var documents = await dbContext.Documents
            .Where(d => d.TenantId == tenantId && d.CaseId == caseId)
            .ToListAsync(cancellationToken);

        return documents.OrderBy(d => d.CreatedAt).ToList();
    }

    public async Task<IReadOnlyList<Document>> ListPendingProcessingAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            return [];
        }

        // Keep ordering client-side for SQLite parity in integration tests. The production
        // worker uses a small batch and a single replica until lease-based claiming is added.
        var pending = await dbContext.Documents
            .AsNoTracking()
            .Where(document =>
                document.Status == DocumentStatus.Quarantined
                || document.Status == DocumentStatus.Validating)
            .ToListAsync(cancellationToken);

        return pending
            .OrderBy(document => document.CreatedAt)
            .ThenBy(document => document.DocumentId)
            .Take(limit)
            .ToList();
    }

    public void Add(Document document) => dbContext.Documents.Add(document);
}
