using Backoffice.Application.Abstractions;
using Backoffice.Application.Cases;
using Backoffice.Application.Documents;
using Backoffice.Domain.Cases;
using Backoffice.Domain.Common;
using Backoffice.Domain.Documents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backoffice.Workers.Tests;

public sealed class DocumentProcessingWorkerTests
{
    [Fact]
    public async Task ProcessBatchOnce_ResumesQuarantinedDocumentAndPromotesIt()
    {
        await using var testServices = await TestServices.CreateAsync();
        var tenantId = "tenant-document-worker";
        var @case = Case.Create(
            tenantId,
            "ext-document-worker-1",
            DisputeType.CardPurchase,
            Channel.App,
            Priority.Normal,
            new Money("BRL", 150m),
            Guid.NewGuid(),
            "test-actor",
            testServices.Clock.UtcNow);
        Document document;

        using (var scope = testServices.ScopeFactory.CreateScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<IDocumentStorage>();
            var stored = await storage.StoreQuarantinedAsync(
                tenantId,
                @case.CaseId,
                "receipt-worker.pdf",
                "worker document content"u8.ToArray());
            document = Document.Register(
                @case.CaseId,
                tenantId,
                DocumentType.Receipt,
                MediaType.ApplicationPdf,
                stored.Checksum,
                stored.StorageReference,
                testServices.Clock.UtcNow);
            @case.Transition(
                @case.CaseVersion,
                CaseState.DocumentsReceived,
                EventTypes.DocumentReceived,
                "test-actor",
                "document-intake",
                Guid.NewGuid(),
                null,
                "Document accepted into quarantine.",
                testServices.Clock.UtcNow);

            scope.ServiceProvider.GetRequiredService<ICaseRepository>().Add(@case);
            scope.ServiceProvider.GetRequiredService<IDocumentRepository>().Add(document);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        var worker = new DocumentProcessingWorker(
            testServices.ScopeFactory,
            NullLogger<DocumentProcessingWorker>.Instance);
        var processed = await worker.ProcessBatchOnceAsync(CancellationToken.None);

        Assert.Equal(1, processed);
        using (var scope = testServices.ScopeFactory.CreateScope())
        {
            var reloaded = await scope.ServiceProvider.GetRequiredService<IDocumentRepository>()
                .FindByIdAsync(tenantId, @case.CaseId, document.DocumentId);
            var reloadedCase = await scope.ServiceProvider.GetRequiredService<ICaseRepository>()
                .FindByIdAsync(tenantId, @case.CaseId);

            Assert.Equal(DocumentStatus.Validated, reloaded!.Status);
            Assert.StartsWith("document-store://accepted/", reloaded.StorageReference);
            Assert.Equal(CaseState.DocumentsValidated, reloadedCase!.State);
        }
    }
}
