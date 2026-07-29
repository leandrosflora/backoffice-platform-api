namespace Backoffice.Application.Documents;

public sealed record StoredDocument(string StorageReference, string Checksum);
public sealed record StoredDocumentContent(byte[] Content, string FileName);

/// <summary>
/// Durable binary storage boundary. New uploads always enter the quarantine zone; only the
/// document-processing workflow may promote a clean file to the accepted zone.
/// </summary>
public interface IDocumentStorage
{
    Task<StoredDocument> StoreQuarantinedAsync(
        string tenantId,
        Guid caseId,
        string fileName,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default);

    Task<StoredDocumentContent> ReadAsync(
        string storageReference,
        CancellationToken cancellationToken = default);

    Task<string> PromoteAsync(
        string quarantineReference,
        CancellationToken cancellationToken = default);
}

public sealed class DocumentProcessingOptions
{
    /// <summary>
    /// Runs processing in the request scope. This is intended only for deterministic tests;
    /// deployed environments use the document-processing worker.
    /// </summary>
    public bool Inline { get; init; }
}
