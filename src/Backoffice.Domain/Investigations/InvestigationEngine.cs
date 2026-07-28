using Backoffice.Domain.Observability;

namespace Backoffice.Domain.Investigations;

/// <summary>
/// Deterministic, rule-based investigation logic — a stand-in for the real
/// investigation-agent runtime described in docs/architecture/component-workflow-orchestrator.md,
/// matching the behavior asserted by evals/datasets/intelligence-v1.jsonl's "investigation"
/// task: no evidence => MISSING_DATA/abstain; any evidence => CONFIRMED_FACT referencing it.
/// </summary>
public static class InvestigationEngine
{
    public static IReadOnlyList<Finding> Run(IReadOnlyList<Guid> evidenceReferences)
    {
        if (evidenceReferences.Count == 0)
        {
            RecordOutcome("missing_data");
            return [new Finding(FindingKind.MissingData, "insufficient-evidence", [])];
        }

        RecordOutcome("confirmed_fact");
        return [new Finding(FindingKind.ConfirmedFact, "transaction-confirmed", evidenceReferences)];
    }

    private static void RecordOutcome(string outcome) =>
        DomainMetrics.IntelligenceOutcomesTotal.Add(1,
            new KeyValuePair<string, object?>("capability", "investigation"),
            new KeyValuePair<string, object?>("outcome", outcome));
}
