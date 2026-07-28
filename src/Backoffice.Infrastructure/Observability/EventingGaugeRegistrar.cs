using System.Diagnostics.Metrics;
using Backoffice.Application.Policy;
using Backoffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backoffice.Infrastructure.Observability;

/// <summary>
/// Registers the `backoffice_outbox_messages`/`backoffice_dead_letters`/`backoffice_timers`
/// gauges (spec: observability-evaluation) — live counts-by-status sampled synchronously at
/// scrape time, not counters, matching the bare (non-`increase()`/non-`_total`) queries in
/// `observability/prometheus/alerts.yml` (e.g. `backoffice_outbox_messages{status=~"PENDING|RETRY"} > 0`).
/// Must be resolved once eagerly at startup (its constructor is what registers the
/// ObservableGauge callbacks with the shared "Backoffice" meter) — see
/// <c>ObservabilityExtensions.AddEventingGauges</c>.
/// </summary>
public sealed class EventingGaugeRegistrar
{
    private static readonly Meter Meter = new("Backoffice");

    public EventingGaugeRegistrar(IServiceScopeFactory scopeFactory)
    {
        Meter.CreateObservableGauge("backoffice_outbox_messages", () =>
        {
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<BackofficeDbContext>();
            return dbContext.Outbox.AsNoTracking().ToList()
                .GroupBy(m => m.Status.ToWireString())
                .Select(g => new Measurement<int>(g.Count(), new KeyValuePair<string, object?>("status", g.Key)));
        });

        Meter.CreateObservableGauge("backoffice_dead_letters", () =>
        {
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<BackofficeDbContext>();
            return dbContext.DeadLetters.AsNoTracking().ToList()
                .GroupBy(d => d.Status.ToWireString())
                .Select(g => new Measurement<int>(g.Count(), new KeyValuePair<string, object?>("status", g.Key)));
        });

        Meter.CreateObservableGauge("backoffice_timers", () =>
        {
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<BackofficeDbContext>();
            return dbContext.Timers.AsNoTracking().ToList()
                .GroupBy(t => t.Status.ToWireString())
                .Select(g => new Measurement<int>(g.Count(), new KeyValuePair<string, object?>("status", g.Key)));
        });
    }
}
