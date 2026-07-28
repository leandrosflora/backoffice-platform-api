using System.Diagnostics;

namespace Backoffice.Application.Observability;

/// <summary>`System.Diagnostics.Activity`/`ActivitySource` are BCL tracing primitives — the
/// same "reference the primitive, not OpenTelemetry" pattern as <c>ApplicationMetrics</c>.
/// The OTel SDK picks up every span from this source via `.AddSource("Backoffice")`,
/// composed only in the Api/Workers hosts (spec: observability-evaluation, "Distributed
/// tracing across API and workers").</summary>
public static class BackofficeActivitySource
{
    public const string Name = "Backoffice";
    public static readonly ActivitySource Instance = new(Name);
}
