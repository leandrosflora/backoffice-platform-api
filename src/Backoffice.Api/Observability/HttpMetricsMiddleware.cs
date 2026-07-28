using System.Diagnostics;
using System.Diagnostics.Metrics;
using Backoffice.Api;
using Microsoft.AspNetCore.Routing;

namespace Backoffice.Api.Observability;

/// <summary>
/// Records `backoffice_http_requests_total{route,status}` and
/// `backoffice_http_request_duration_seconds{route}` (spec: observability-evaluation) and
/// tags the current ASP.NET Core-instrumentation span with `case_id`/`tenant_id`/
/// `correlation_id` from the request. Route is the low-cardinality route *template*
/// (`/v1/cases/{caseId:guid}/timeline`), never the raw path with a real id — the spec
/// explicitly forbids caseId/tenantId as metric label values.
/// </summary>
public sealed class HttpMetricsMiddleware(RequestDelegate next)
{
    private static readonly Meter Meter = new("Backoffice");
    private static readonly Counter<long> RequestsTotal = Meter.CreateCounter<long>("backoffice_http_requests_total");
    private static readonly Histogram<double> RequestDurationSeconds =
        Meter.CreateHistogram<double>("backoffice_http_request_duration_seconds", unit: "s");

    public async Task InvokeAsync(HttpContext context)
    {
        var activity = Activity.Current;
        if (context.Request.Headers.TryGetValue(RequestContext.TenantHeader, out var tenantId))
        {
            activity?.SetTag("tenant_id", tenantId.ToString());
        }
        if (context.Request.Headers.TryGetValue(RequestContext.CorrelationHeader, out var correlationId))
        {
            activity?.SetTag("correlation_id", correlationId.ToString());
        }
        if (context.GetRouteValue("caseId") is { } caseId)
        {
            activity?.SetTag("case_id", caseId.ToString());
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await next(context);
        }
        finally
        {
            stopwatch.Stop();
            var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unmatched";
            var status = context.Response.StatusCode.ToString();

            RequestsTotal.Add(1,
                new KeyValuePair<string, object?>("route", route),
                new KeyValuePair<string, object?>("status", status));
            RequestDurationSeconds.Record(stopwatch.Elapsed.TotalSeconds,
                new KeyValuePair<string, object?>("route", route));
        }
    }
}
