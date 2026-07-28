using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Backoffice.Application.Cases;
using Backoffice.Application.Eventing;
using Backoffice.Domain.Cases;
using Backoffice.Domain.Eventing;
using Backoffice.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Backoffice.Api.Tests;

/// <summary>
/// Verifies the real Prometheus scrape endpoint actually exposes metrics under the exact
/// documented names (spec: observability-evaluation, "Metrics matching documented names and
/// semantics"), driven through real HTTP requests rather than inspecting instrument
/// registrations directly.
/// </summary>
public class MetricsEndpointTests(BackofficeApiFactory factory) : IClassFixture<BackofficeApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(ScreamingSnakeCaseNamingPolicy.Instance) },
    };

    [Fact]
    public async Task MetricsEndpoint_AfterActivity_ExposesDocumentedMetricNames()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(RequestContext.TenantHeader, "tenant-metrics");
        client.DefaultRequestHeaders.Add(RequestContext.SubjectHeader, "test-actor");
        client.DefaultRequestHeaders.Add(RequestContext.RolesHeader, "case-manager");

        // Drives real http_requests/policy_decisions/cases_created activity.
        var request = new CreateCaseRequest("ext-metrics-1", DisputeType.CardPurchase, Channel.App, Priority.Normal, new MoneyDto("BRL", "150.00"));
        var createResponse = await client.PostAsJsonAsync("/v1/cases", request, JsonOptions);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = (await createResponse.Content.ReadFromJsonAsync<CaseResponse>(JsonOptions))!;

        // backoffice_timers only emits a series once a row exists — schedule one now, while
        // the case is still non-terminal (timer.schedule denies on a terminal resource.state).
        var timerResponse = await client.PostAsJsonAsync(
            $"/v1/operations/cases/{created.CaseId}/timers",
            new ScheduleTimerRequest("CASE_EXPIRY", 60, null),
            JsonOptions);
        Assert.Equal(HttpStatusCode.OK, timerResponse.StatusCode);

        // Case.Create's initial state isn't itself a "transition" (no from_state) — cancel it
        // to drive a real Case.Transition call and populate backoffice_workflow_transitions_total.
        var cancelRequest = new HttpRequestMessage(HttpMethod.Post, $"/v1/cases/{created.CaseId}/cancel")
        {
            Content = JsonContent.Create(new { reason = "driving a real transition for the metrics test" }, options: JsonOptions),
        };
        cancelRequest.Headers.TryAddWithoutValidation("If-Match", created.CaseVersion.ToString());
        var cancelResponse = await client.SendAsync(cancelRequest);
        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);

        // backoffice_dead_letters similarly needs at least one row — no HTTP path produces
        // one without the background workers, so seed it directly.
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<BackofficeDbContext>();
            dbContext.DeadLetters.Add(DeadLetter.Create(
                "consumer", "backoffice.events.v1", Guid.NewGuid(), "tenant-metrics", Guid.NewGuid(), "TimerFired",
                "{}", "seeded for metrics test", 3, DateTimeOffset.UtcNow));
            await dbContext.SaveChangesAsync();
        }

        var metricsResponse = await client.GetAsync("/metrics");
        Assert.Equal(HttpStatusCode.OK, metricsResponse.StatusCode);
        var body = await metricsResponse.Content.ReadAsStringAsync();

        // Counters/histograms this request path should have just recorded.
        Assert.Contains("backoffice_http_requests_total", body);
        Assert.Contains("backoffice_http_request_duration_seconds", body);
        Assert.Contains("backoffice_policy_decisions_total", body);
        Assert.Contains("backoffice_policy_decision_duration_seconds", body);
        Assert.Contains("backoffice_workflow_transitions_total", body);
        Assert.Contains("backoffice_cases_created_total", body);

        // Gauges are sampled at scrape time regardless of prior activity.
        Assert.Contains("backoffice_outbox_messages", body);
        Assert.Contains("backoffice_dead_letters", body);
        Assert.Contains("backoffice_timers", body);
    }
}
