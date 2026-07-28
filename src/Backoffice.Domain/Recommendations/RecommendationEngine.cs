using Backoffice.Domain.Investigations;

namespace Backoffice.Domain.Recommendations;

public sealed record RecommendationDecision(
    RecommendationOutcome Outcome, double Confidence, string Rationale, IReadOnlyList<string> RuleReferences);

/// <summary>
/// Deterministic, rule-based recommendation logic matching evals/datasets/intelligence-v1.jsonl's
/// "recommendation" task: only a CONFIRMED_FACT finding backed by non-empty evidence yields
/// APPROVE; anything else (missing/inconclusive findings, or no evidence at all) abstains
/// rather than guessing (spec: investigation-decision, "Deterministic abstention on
/// ungrounded recommendations").
/// </summary>
public static class RecommendationEngine
{
    public static RecommendationDecision Decide(IReadOnlyList<Finding> findings, IReadOnlyList<Guid> evidenceReferences)
    {
        var isGrounded = evidenceReferences.Count > 0 && findings.Any(f => f.Kind == FindingKind.ConfirmedFact);

        return isGrounded
            ? new RecommendationDecision(
                RecommendationOutcome.Approve, 0.9, "Transaction confirmed by grounding evidence.", ["BR-008"])
            : new RecommendationDecision(
                RecommendationOutcome.Abstain, 0.0, "Insufficient grounding to recommend an outcome.", ["BR-009"]);
    }
}
