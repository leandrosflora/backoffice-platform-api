using Backoffice.Domain.Investigations;

namespace Backoffice.Application.Investigations;

public enum RequestedCheck
{
    TransactionLookup,
    FraudSignalLookup,
    CustomerHistory,
    DocumentConsistency,
}

public sealed record StartInvestigationRequest(IReadOnlyList<RequestedCheck> RequestedChecks);

public sealed record FindingResponse(FindingKind Kind, string Summary, IReadOnlyList<Guid> EvidenceReferences);

public sealed record InvestigationResponse(
    Guid InvestigationId,
    Guid CaseId,
    InvestigationStatus Status,
    IReadOnlyList<FindingResponse> Findings,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

public static class InvestigationMapping
{
    public static InvestigationResponse ToResponse(this Investigation investigation) => new(
        investigation.InvestigationId,
        investigation.CaseId,
        investigation.Status,
        investigation.Findings.Select(f => new FindingResponse(f.Kind, f.Summary, f.EvidenceReferences)).ToList(),
        investigation.CreatedAt,
        investigation.CompletedAt);
}
