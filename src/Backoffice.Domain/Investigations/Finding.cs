namespace Backoffice.Domain.Investigations;

public sealed record Finding(FindingKind Kind, string Summary, IReadOnlyList<Guid> EvidenceReferences);
