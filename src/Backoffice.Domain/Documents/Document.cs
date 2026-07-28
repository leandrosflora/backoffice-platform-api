namespace Backoffice.Domain.Documents;

public sealed class InvalidDocumentTransitionException(DocumentStatus from, DocumentStatus to)
    : Exception($"Document transition from '{from}' to '{to}' is not allowed.")
{
}

/// <summary>
/// A document registered against a case, already residing in quarantine storage at
/// registration time (contracts/openapi/paths/documents-evidence.yaml: "Registra um
/// documento previamente armazenado em área de quarentena"). Checksum is supplied by the
/// caller (SHA-256 hex of the stored blob), not computed here — this endpoint registers
/// metadata about an already-stored object, it does not accept a binary upload.
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
    public string StorageReference { get; private init; } = string.Empty;
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

    public void MarkValidated()
    {
        if (Status != DocumentStatus.Validating)
        {
            throw new InvalidDocumentTransitionException(Status, DocumentStatus.Validated);
        }

        Status = DocumentStatus.Validated;
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
