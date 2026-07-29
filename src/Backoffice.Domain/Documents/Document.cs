namespace Backoffice.Domain.Documents;

public sealed class InvalidDocumentTransitionException(DocumentStatus from, DocumentStatus to)
    : Exception($"Document transition from '{from}' to '{to}' is not allowed.")
{
}

/// <summary>
/// A document registered against a case after its binary has been durably written to
/// quarantine storage. The application computes the checksum from the stored bytes and
/// controls promotion to accepted storage after malware scanning.
/// </summary>
public sealed class Document
{
    public Guid DocumentId { get; private init; }
    public Guid CaseId { get; private init; }
    public string TenantId { get; private init; } = string.Empty;
    public DocumentType DocumentType { get; private init; }
    public DocumentStatus Status { get; private set; }
    public MediaType MediaType { get; private init; }
    public string Checksum { get; private init; } = string.Empty;
    public int Version { get; private init; }
    public string StorageReference { get; private set; } = string.Empty;
    public List<string> RejectionReasons { get; private init; } = [];
    public DateTimeOffset CreatedAt { get; private init; }

    private Document() { }

    public static Document Register(
        Guid caseId,
        string tenantId,
        DocumentType documentType,
        MediaType mediaType,
        string checksum,
        string storageReference,
        DateTimeOffset now) => new()
    {
        DocumentId = Guid.NewGuid(),
        CaseId = caseId,
        TenantId = tenantId,
        DocumentType = documentType,
        Status = DocumentStatus.Quarantined,
        MediaType = mediaType,
        Checksum = checksum,
        Version = 1,
        StorageReference = storageReference,
        CreatedAt = now,
    };

    public void ClearQuarantine()
    {
        if (Status != DocumentStatus.Quarantined)
        {
            throw new InvalidDocumentTransitionException(Status, DocumentStatus.Validating);
        }

        Status = DocumentStatus.Validating;
    }

    public void MarkValidated(string acceptedStorageReference)
    {
        if (Status != DocumentStatus.Validating)
        {
            throw new InvalidDocumentTransitionException(Status, DocumentStatus.Validated);
        }

        StorageReference = acceptedStorageReference;
        Status = DocumentStatus.Validated;
    }

    public void RequireReview(string acceptedStorageReference)
    {
        if (Status != DocumentStatus.Validating)
        {
            throw new InvalidDocumentTransitionException(Status, DocumentStatus.ReviewRequired);
        }

        StorageReference = acceptedStorageReference;
        Status = DocumentStatus.ReviewRequired;
    }

    public void Reject(IEnumerable<string> reasons)
    {
        if (Status is not (DocumentStatus.Quarantined or DocumentStatus.Validating))
        {
            throw new InvalidDocumentTransitionException(Status, DocumentStatus.Rejected);
        }

        Status = DocumentStatus.Rejected;
        RejectionReasons.AddRange(reasons);
    }
}
