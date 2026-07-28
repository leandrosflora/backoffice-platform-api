namespace Backoffice.Domain.Investigations;

public sealed class Investigation
{
    public Guid InvestigationId { get; private init; }
    public Guid CaseId { get; private init; }
    public string TenantId { get; private init; } = string.Empty;
    public InvestigationStatus Status { get; private set; }
    public List<Finding> Findings { get; private init; } = [];
    public DateTimeOffset CreatedAt { get; private init; }
    public DateTimeOffset? CompletedAt { get; private set; }

    private Investigation() { }

    public static Investigation Complete(
        Guid caseId, string tenantId, IReadOnlyList<Finding> findings, DateTimeOffset now) => new()
    {
        InvestigationId = Guid.NewGuid(),
        CaseId = caseId,
        TenantId = tenantId,
        Status = InvestigationStatus.Completed,
        Findings = findings.ToList(),
        CreatedAt = now,
        CompletedAt = now,
    };
}
