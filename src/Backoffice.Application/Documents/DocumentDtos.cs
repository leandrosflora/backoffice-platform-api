using Backoffice.Domain.Documents;
using Backoffice.Domain.Evidence;

namespace Backoffice.Application.Documents;

public sealed record RegisterDocumentRequest(
    DocumentType DocumentType,
    MediaType MediaType,
    string Checksum,
    string StorageReference);

public sealed record DocumentResponse(
    Guid DocumentId,
    Guid CaseId,
    string TenantId,
    DocumentType DocumentType,
    DocumentStatus Status,
    MediaType MediaType,
    string Checksum,
    int Version,
    string StorageReference,
    IReadOnlyList<string> RejectionReasons,
    DateTimeOffset CreatedAt);

public sealed record EvidenceResponse(
    Guid EvidenceId,
    Guid CaseId,
    string TenantId,
    EvidenceType EvidenceType,
    EvidenceSourceType SourceType,
    string SourceReference,
    string SourceVersion,
    string? Value,
    double Confidence,
    int? Page,
    string? Position,
    string? Checksum,
    DateTimeOffset CreatedAt);

public static class DocumentMapping
{
    public static DocumentResponse ToResponse(this Document document) => new(
        document.DocumentId,
        document.CaseId,
        document.TenantId,
        document.DocumentType,
        document.Status,
        document.MediaType,
        document.Checksum,
        document.Version,
        document.StorageReference,
        document.RejectionReasons,
        document.CreatedAt);

    public static EvidenceResponse ToResponse(this EvidenceRecord evidence) => new(
        evidence.EvidenceId,
        evidence.CaseId,
        evidence.TenantId,
        evidence.EvidenceType,
        evidence.SourceType,
        evidence.SourceReference,
        evidence.SourceVersion,
        evidence.Value,
        evidence.Confidence,
        evidence.Page,
        evidence.Position,
        evidence.Checksum,
        evidence.CreatedAt);
}
