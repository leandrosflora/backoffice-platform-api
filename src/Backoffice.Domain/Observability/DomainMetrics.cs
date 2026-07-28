using System.Diagnostics.Metrics;

namespace Backoffice.Domain.Observability;

/// <summary>
/// Metric instruments recorded directly from domain logic. `System.Diagnostics.Metrics` is a
/// BCL primitive (part of System.Diagnostics.DiagnosticSource), not an OpenTelemetry/
/// Infrastructure dependency, so the Domain layer can reference it without violating Clean
/// Architecture layering — the actual OTel wiring (exporters, `.AddMeter("Backoffice")`) is
/// composed only in Backoffice.Api (spec: observability-evaluation, "Metrics matching
/// documented names and semantics"). All meters across layers share the name "Backoffice" so
/// one `.AddMeter("Backoffice")` registration picks up every instrument regardless of which
/// assembly created it.
/// </summary>
public static class DomainMetrics
{
    private static readonly Meter Meter = new("Backoffice");

    public static readonly Counter<long> WorkflowTransitionsTotal =
        Meter.CreateCounter<long>("backoffice_workflow_transitions_total");

    public static readonly Counter<long> IntelligenceOutcomesTotal =
        Meter.CreateCounter<long>("backoffice_intelligence_outcomes_total");
}
