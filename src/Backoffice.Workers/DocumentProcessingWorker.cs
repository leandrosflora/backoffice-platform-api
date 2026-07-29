using Backoffice.Application.Documents;

namespace Backoffice.Workers;

/// <summary>
/// Polls durable QUARANTINED/VALIDATING documents and runs malware scanning plus document
/// intelligence outside the upload request. This baseline intentionally runs as one replica;
/// lease-based claiming is required before horizontally scaling this role.
/// </summary>
public sealed class DocumentProcessingWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<DocumentProcessingWorker> logger) : BackgroundService
{
    private const int BatchLimit = 10;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to load pending documents; the batch will be retried");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    internal async Task<int> ProcessBatchOnceAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Backoffice.Domain.Documents.Document> pending;
        using (var discoveryScope = scopeFactory.CreateScope())
        {
            var repository = discoveryScope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            pending = await repository.ListPendingProcessingAsync(BatchLimit, cancellationToken);
        }

        foreach (var candidate in pending)
        {
            try
            {
                using var processingScope = scopeFactory.CreateScope();
                var handler = processingScope.ServiceProvider.GetRequiredService<ProcessDocumentHandler>();
                await handler.HandleAsync(
                    candidate.TenantId,
                    candidate.CaseId,
                    candidate.DocumentId,
                    "document-processing-worker",
                    Guid.NewGuid(),
                    cancellationToken);

                logger.LogInformation(
                    "Processed document {DocumentId} for case {CaseId}",
                    candidate.DocumentId,
                    candidate.CaseId);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // The handler persists VALIDATING before external calls. Leaving that state
                // untouched is deliberate: the next poll safely retries a scanner/storage/AI
                // outage instead of accepting the document.
                logger.LogWarning(
                    exception,
                    "Document {DocumentId} processing failed and will be retried",
                    candidate.DocumentId);
            }
        }

        return pending.Count;
    }
}
