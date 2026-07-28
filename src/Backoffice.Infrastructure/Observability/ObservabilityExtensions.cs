using Backoffice.Application.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Backoffice.Infrastructure.Observability;

public static class ObservabilityExtensions
{
    /// <summary>
    /// Wires OpenTelemetry tracing (every host) and, for the Api host only, metrics behind a
    /// Prometheus scrape endpoint (spec: observability-evaluation, "Distributed tracing
    /// across API and workers" / "Metrics matching documented names and semantics"). Traces
    /// export via OTLP to the collector in `observability/otel/collector-config.yaml`;
    /// `caseId`/`tenantId`/`correlationId` are set as span tags at the call sites (Policy
    /// enforcement, worker processing steps, and the Api's request-tagging middleware) —
    /// never as metric labels, which stay low-cardinality (route/status/action/decision/etc).
    /// </summary>
    public static IServiceCollection AddObservability(
        this IServiceCollection services, IConfiguration configuration, string serviceName, bool isHttpHost)
    {
        var otlpEndpoint = new Uri(configuration["Otel:Endpoint"] ?? "http://localhost:4317");

        var builder = services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing =>
            {
                tracing.AddSource(BackofficeActivitySource.Name);
                if (isHttpHost)
                {
                    tracing.AddAspNetCoreInstrumentation();
                }
                tracing.AddHttpClientInstrumentation();
                tracing.AddOtlpExporter(o => o.Endpoint = otlpEndpoint);
            });

        if (isHttpHost)
        {
            builder.WithMetrics(metrics => metrics
                .AddMeter("Backoffice")
                .AddPrometheusExporter());
        }

        return services;
    }

    /// <summary>Eagerly resolves <see cref="EventingGaugeRegistrar"/> so its constructor runs
    /// and registers the outbox/dead-letter/timer ObservableGauges before the first scrape.
    /// Api-only, alongside <see cref="AddObservability"/>'s metrics pipeline.</summary>
    public static IServiceCollection AddEventingGauges(this IServiceCollection services)
    {
        services.AddSingleton<EventingGaugeRegistrar>();
        return services;
    }
}
