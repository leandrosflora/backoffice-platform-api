namespace Backoffice.Domain.Recommendations;

public sealed class Recommendation
{
    public Guid RecommendationId { get; private init; }
    public Guid CaseId { get; private init; }
    public string TenantId { get; private init; } = string.Empty;
    public long CaseVersion { get; private init; }
    public long RecommendationVersion { get; private init; }
    public RecommendationOutcome Outcome { get; private init; }
    public double Confidence { get; private init; }
    public string Rationale { get; private init; } = string.Empty;
    public List<Guid> EvidenceReferences { get; private init; } = [];
    public List<string> RuleReferences { get; private init; } = [];
    public string CreatedBy { get; private init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private init; }

    private Recommendation() { }

    public static Recommendation Create(
        Guid caseId,
        string tenantId,
        long caseVersion,
        long recommendationVersion,
        RecommendationOutcome outcome,
        double confidence,
        string rationale,
        IReadOnlyList<Guid> evidenceReferences,
        IReadOnlyList<string> ruleReferences,
        string createdBy,
        DateTimeOffset now) => new()
    {
        RecommendationId = Guid.NewGuid(),
        CaseId = caseId,
        TenantId = tenantId,
        CaseVersion = caseVersion,
        RecommendationVersion = recommendationVersion,
        Outcome = outcome,
        Confidence = confidence,
        Rationale = rationale,
        EvidenceReferences = evidenceReferences.ToList(),
        RuleReferences = ruleReferences.ToList(),
        CreatedBy = createdBy,
        CreatedAt = now,
    };
}
