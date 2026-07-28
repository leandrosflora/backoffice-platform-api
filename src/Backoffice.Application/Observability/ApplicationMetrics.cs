using System.Diagnostics.Metrics;

namespace Backoffice.Application.Observability;

/// <summary>See Backoffice.Domain.Observability.DomainMetrics for why this layer can define
/// instruments directly via the BCL System.Diagnostics.Metrics API.</summary>
public static class ApplicationMetrics
{
    private static readonly Meter Meter = new("Backoffice");

    public static readonly Counter<long> PolicyDecisionsTotal =
        Meter.CreateCounter<long>("backoffice_policy_decisions_total");

    public static readonly Histogram<double> PolicyDecisionDurationSeconds =
        Meter.CreateHistogram<double>("backoffice_policy_decision_duration_seconds", unit: "s");

    public static readonly Counter<long> ExecutionsTotal =
        Meter.CreateCounter<long>("backoffice_executions_total");

    public static readonly Counter<long> ReconciliationsTotal =
        Meter.CreateCounter<long>("backoffice_reconciliations_total");

    public static readonly Counter<long> IdempotencyTotal =
        Meter.CreateCounter<long>("backoffice_idempotency_total");

    public static readonly Counter<long> CasesCreatedTotal =
        Meter.CreateCounter<long>("backoffice_cases_created_total");
}
