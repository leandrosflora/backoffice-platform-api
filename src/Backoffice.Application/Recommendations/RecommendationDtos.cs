using Backoffice.Domain.Recommendations;

namespace Backoffice.Application.Recommendations;

public sealed record CreateRecommendationRequest(long CaseVersion, Guid InvestigationId);

public sealed record RecommendationResponse(
    Guid RecommendationId,
    Guid CaseId,
    long CaseVersion,
    long RecommendationVersion,
    RecommendationOutcome Outcome,
    double Confidence,
    string Rationale,
    IReadOnlyList<Guid> EvidenceReferences,
    IReadOnlyList<string> RuleReferences,
    string CreatedBy,
    DateTimeOffset CreatedAt);

public static class RecommendationMapping
{
    public static RecommendationResponse ToResponse(this Recommendation recommendation) => new(
        recommendation.RecommendationId,
        recommendation.CaseId,
        recommendation.CaseVersion,
        recommendation.RecommendationVersion,
        recommendation.Outcome,
        recommendation.Confidence,
        recommendation.Rationale,
        recommendation.EvidenceReferences,
        recommendation.RuleReferences,
        recommendation.CreatedBy,
        recommendation.CreatedAt);
}
