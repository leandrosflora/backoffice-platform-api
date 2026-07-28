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
/// HTTP-level coverage for contracts/openapi/eventing-operations-api.yaml, gated by the real
/// OPA policy decision point (spec: eventing-reliability task 8.6, policy-authorization).
/// Dead-letter rows are seeded directly via the DbContext, since producing one through the
/// API alone would require the background workers this factory doesn't host.
/// </summary>
public class OperationsEndpointsTests(BackofficeApiFactory factory) : IClassFixture<BackofficeApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(ScreamingSnakeCaseNamingPolicy.Instance) },
    };

    private HttpClient CreateClient(string tenantId, string roles)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(RequestContext.TenantHeader, tenantId);
        client.DefaultRequestHeaders.Add(RequestContext.SubjectHeader, "operator-1");
        client.DefaultRequestHeaders.Add(RequestContext.RolesHeader, roles);
        return client;
    }

    private static async Task<CaseResponse> CreateCaseAsync(HttpClient client, string externalReference)
    {
        var request = new CreateCaseRequest(externalReference, DisputeType.CardPurchase, Channel.App, Priority.Normal, new MoneyDto("BRL", "150.00"));
        var response = await client.PostAsJsonAsync("/v1/cases", request, JsonOptions);
        return (await response.Content.ReadFromJsonAsync<CaseResponse>(JsonOptions))!;
    }

    [Fact]
    public async Task ScheduleTimer_AllowedRole_ReturnsScheduledTimer()
    {
        var client = CreateClient("tenant-ops-timer-ok", "case-manager,platform-operator");
        var @case = await CreateCaseAsync(client, "ext-ops-timer-ok-1");

        var response = await client.PostAsJsonAsync(
            $"/v1/operations/cases/{@case.CaseId}/timers",
            new ScheduleTimerRequest("CASE_EXPIRY", 60, null),
            JsonOptions);
        var timer = await response.Content.ReadFromJsonAsync<TimerResponse>(JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("CASE_EXPIRY", timer!.TimerType);
        Assert.Equal(TimerStatus.Scheduled, timer.Status);
    }

    [Fact]
    public async Task ScheduleTimer_RoleNotAuthorized_ReturnsForbidden()
    {
        // "auditor" can read operations state but isn't in {case-manager, platform-operator}
        // for timer.schedule (policies/authorization.rego).
        var client = CreateClient("tenant-ops-timer-forbidden", "case-manager,auditor");
        var @case = await CreateCaseAsync(client, "ext-ops-timer-forbidden-1");

        var otherClient = CreateClient("tenant-ops-timer-forbidden", "auditor");
        var response = await otherClient.PostAsJsonAsync(
            $"/v1/operations/cases/{@case.CaseId}/timers",
            new ScheduleTimerRequest("CASE_EXPIRY", 60, null),
            JsonOptions);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListOutbox_AllowedRole_ReturnsRowsForTenant()
    {
        var client = CreateClient("tenant-ops-outbox", "case-manager,platform-operator");
        await CreateCaseAsync(client, "ext-ops-outbox-1"); // CaseCreated -> one outbox row

        var response = await client.GetAsync("/v1/operations/outbox?limit=10");
        var rows = await response.Content.ReadFromJsonAsync<List<OutboxMessageResponse>>(JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(rows!);
        Assert.All(rows!, r => Assert.Equal("tenant-ops-outbox", r.TenantId));
    }

    [Fact]
    public async Task ListDeadLetters_AllowedRole_ReturnsSeededRow()
    {
        var client = CreateClient("tenant-ops-dl-list", "auditor");
        var tenantId = "tenant-ops-dl-list";
        await SeedDeadLetterAsync(tenantId);

        var response = await client.GetAsync("/v1/operations/dead-letters?limit=10");
        var rows = await response.Content.ReadFromJsonAsync<List<DeadLetterResponse>>(JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(rows!);
        Assert.Equal(DeadLetterStatus.Open, rows![0].Status);
    }

    [Fact]
    public async Task ReplayDeadLetter_OpenDeadLetter_TransitionsToReplayedAndEnqueuesOutboxRow()
    {
        var tenantId = "tenant-ops-replay-ok";
        var (deadLetterId, originalEventId) = await SeedDeadLetterAsync(tenantId);

        var client = CreateClient(tenantId, "platform-operator");
        var response = await client.PostAsJsonAsync(
            $"/v1/operations/dead-letters/{deadLetterId}/replay",
            new ReplayDeadLetterRequest("confirmed root cause fixed, safe to replay"),
            JsonOptions);
        var body = await response.Content.ReadFromJsonAsync<ReplayDeadLetterResponse>(JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("REPLAYED", body!.Status);
        Assert.NotEqual(originalEventId, body.ReplayEventId);

        var outboxResponse = await client.GetAsync("/v1/operations/outbox?limit=10");
        var outboxRows = await outboxResponse.Content.ReadFromJsonAsync<List<OutboxMessageResponse>>(JsonOptions);
        Assert.Contains(outboxRows!, r => r.EventId == body.ReplayEventId);
    }

    [Fact]
    public async Task ReplayDeadLetter_AlreadyReplayed_IsRejectedByPolicy()
    {
        var tenantId = "tenant-ops-replay-twice";
        var (deadLetterId, _) = await SeedDeadLetterAsync(tenantId);
        var client = CreateClient(tenantId, "platform-operator");

        var first = await client.PostAsJsonAsync(
            $"/v1/operations/dead-letters/{deadLetterId}/replay",
            new ReplayDeadLetterRequest("first replay after confirming the fix"),
            JsonOptions);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // policies/authorization.rego's event.replay rule requires resource.state == "OPEN";
        // the dead letter is now REPLAYED, so OPA denies (403) before the handler's own
        // DeadLetterAlreadyReplayedException (409) guard is ever reached.
        var second = await client.PostAsJsonAsync(
            $"/v1/operations/dead-letters/{deadLetterId}/replay",
            new ReplayDeadLetterRequest("second replay attempt should be rejected"),
            JsonOptions);

        Assert.Equal(HttpStatusCode.Forbidden, second.StatusCode);
    }

    private async Task<(long DeadLetterId, Guid EventId)> SeedDeadLetterAsync(string tenantId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BackofficeDbContext>();

        var seededEventId = Guid.NewGuid();
        var envelopeJson = JsonSerializer.Serialize(new
        {
            eventId = seededEventId,
            eventType = "TimerFired",
            payload = new { timerId = Guid.NewGuid(), timerType = "CASE_EXPIRY" },
        });

        var deadLetter = DeadLetter.Create(
            "consumer", "backoffice.events.v1", seededEventId, tenantId, Guid.NewGuid(), "TimerFired",
            envelopeJson, "simulated processing failure for test seeding", 3, DateTimeOffset.UtcNow);
        dbContext.DeadLetters.Add(deadLetter);
        await dbContext.SaveChangesAsync();

        return (deadLetter.Id, seededEventId);
    }
}
