namespace Backoffice.Domain.Evidence;

/// <summary>
/// A unit of evidence backing an investigation/recommendation. Named EvidenceRecord (not
/// Evidence) to avoid clashing with the EvidenceType/sibling naming in this namespace.
/// References its source by id + version only (never embeds document content), per
/// spec: document-intelligence, "Evidence extraction and mapping".
/// </summary>
public sealed class EvidenceRecord
{
    public Guid EvidenceId { get; private init; }
    public Guid CaseId { get; private init; }
    public string TenantId { get; private init; } = string.Empty;
    public EvidenceType EvidenceType { get; private init; }
    public EvidenceSourceType SourceType { get; private init; }
    public string SourceReference { get; private init; } = string.Empty;
    public string SourceVersion { get; private init; } = string.Empty;
    public string? Value { get; private init; }
    public double Confidence { get; private init; }
    public int? Page { get; private init; }
    public string? Position { get; private init; }
    public string? Checksum { get; private init; }
    public DateTimeOffset CreatedAt { get; private init; }

    private EvidenceRecord() { }

    public static EvidenceRecord Create(
        Guid caseId,
        string tenantId,
        EvidenceType evidenceType,
        EvidenceSourceType sourceType,
        string sourceReference,
        string sourceVersion,
        double confidence,
        string? value,
        string? checksum,
        DateTimeOffset now) => new()
    {
        EvidenceId = Guid.NewGuid(),
        CaseId = caseId,
        TenantId = tenantId,
        EvidenceType = evidenceType,
        SourceType = sourceType,
        SourceReference = sourceReference,
        SourceVersion = sourceVersion,
        Confidence = confidence,
        Value = value,
        Checksum = checksum,
        CreatedAt = now,
    };
}
