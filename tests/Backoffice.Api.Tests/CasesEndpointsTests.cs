using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Backoffice.Api.Cases;
using Backoffice.Application.Cases;
using Backoffice.Domain.Cases;

namespace Backoffice.Api.Tests;

public class CasesEndpointsTests(BackofficeApiFactory factory) : IClassFixture<BackofficeApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(ScreamingSnakeCaseNamingPolicy.Instance) },
    };

    private HttpClient CreateClient(string tenantId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(RequestContext.TenantHeader, tenantId);
        client.DefaultRequestHeaders.Add(RequestContext.SubjectHeader, "test-actor");
        // case-manager covers create/read/cancel; auditor covers the timeline (audit.read).
        // A single test actor holding both roles is a simplification — real deployments
        // would segregate these (spec: policy-authorization).
        client.DefaultRequestHeaders.Add(RequestContext.RolesHeader, "case-manager,auditor");
        return client;
    }

    private static CreateCaseRequest NewCaseRequest(string externalReference) => new(
        externalReference,
        DisputeType.CardPurchase,
        Channel.App,
        Priority.Normal,
        new MoneyDto("BRL", "150.00"));

    private static Task<HttpResponseMessage> PostCaseAsync(HttpClient client, CreateCaseRequest request) =>
        client.PostAsJsonAsync("/v1/cases", request, JsonOptions);

    private static Task<CaseResponse?> ReadCaseAsync(HttpContent content) =>
        content.ReadFromJsonAsync<CaseResponse>(JsonOptions);

    [Fact]
    public async Task CreateCase_DuplicateExternalReference_ReturnsSameCase()
    {
        var client = CreateClient("tenant-dup");
        var request = NewCaseRequest("ext-dup-1");

        var first = await PostCaseAsync(client, request);
        var firstBody = await ReadCaseAsync(first.Content);

        var second = await PostCaseAsync(client, request);
        var secondBody = await ReadCaseAsync(second.Content);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(firstBody!.CaseId, secondBody!.CaseId);
        Assert.Equal(firstBody.CaseVersion, secondBody.CaseVersion);
    }

    [Fact]
    public async Task CreateCase_StartsInCreatedState()
    {
        var client = CreateClient("tenant-create");

        var response = await PostCaseAsync(client, NewCaseRequest("ext-create-1"));
        var body = await ReadCaseAsync(response.Content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(CaseState.Created, body!.State);
        Assert.Equal(1, body.CaseVersion);
        Assert.Equal("150.00", body.DisputedAmount.Amount);
    }

    [Fact]
    public async Task GetCase_CrossTenant_ReturnsNotFound()
    {
        var ownerClient = CreateClient("tenant-owner");
        var created = await ReadCaseAsync((await PostCaseAsync(ownerClient, NewCaseRequest("ext-cross-1"))).Content);

        var otherTenantClient = CreateClient("tenant-other");
        var response = await otherTenantClient.GetAsync($"/v1/cases/{created!.CaseId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CancelCase_WithStaleIfMatch_ReturnsConflict()
    {
        var client = CreateClient("tenant-stale");
        var created = await ReadCaseAsync((await PostCaseAsync(client, NewCaseRequest("ext-stale-1"))).Content);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/cases/{created!.CaseId}/cancel")
        {
            Content = JsonContent.Create(new CancelCaseBody("no longer needed")),
        };
        request.Headers.TryAddWithoutValidation("If-Match", "999");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CancelCase_WithCorrectIfMatch_TransitionsToCancelled()
    {
        var client = CreateClient("tenant-cancel");
        var created = await ReadCaseAsync((await PostCaseAsync(client, NewCaseRequest("ext-cancel-1"))).Content);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/cases/{created!.CaseId}/cancel")
        {
            Content = JsonContent.Create(new CancelCaseBody("customer withdrew dispute")),
        };
        request.Headers.TryAddWithoutValidation("If-Match", created.CaseVersion.ToString());

        var response = await client.SendAsync(request);
        var body = await ReadCaseAsync(response.Content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(CaseState.Cancelled, body!.State);
    }

    [Fact]
    public async Task CancelCase_AlreadyCancelled_IsRejectedByPolicy()
    {
        var client = CreateClient("tenant-double-cancel");
        var created = await ReadCaseAsync((await PostCaseAsync(client, NewCaseRequest("ext-double-cancel-1"))).Content);

        var firstCancel = new HttpRequestMessage(HttpMethod.Post, $"/v1/cases/{created!.CaseId}/cancel")
        {
            Content = JsonContent.Create(new CancelCaseBody("first cancel")),
        };
        firstCancel.Headers.TryAddWithoutValidation("If-Match", created.CaseVersion.ToString());
        var firstResponse = await client.SendAsync(firstCancel);
        var cancelled = await ReadCaseAsync(firstResponse.Content);

        var secondCancel = new HttpRequestMessage(HttpMethod.Post, $"/v1/cases/{created.CaseId}/cancel")
        {
            Content = JsonContent.Create(new CancelCaseBody("second cancel")),
        };
        secondCancel.Headers.TryAddWithoutValidation("If-Match", cancelled!.CaseVersion.ToString());
        var secondResponse = await client.SendAsync(secondCancel);

        // policies/authorization.rego's case.cancel rule only allows a fixed set of
        // pre-cancellation states; CANCELLED isn't among them, so OPA denies (403) before
        // the domain's own invalid-transition guard (409) is ever reached.
        Assert.Equal(HttpStatusCode.Forbidden, secondResponse.StatusCode);
    }

    [Fact]
    public async Task Timeline_ReturnsEntriesInVersionOrder()
    {
        var client = CreateClient("tenant-timeline");
        var created = await ReadCaseAsync((await PostCaseAsync(client, NewCaseRequest("ext-timeline-1"))).Content);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/cases/{created!.CaseId}/cancel")
        {
            Content = JsonContent.Create(new CancelCaseBody("test cancel")),
        };
        request.Headers.TryAddWithoutValidation("If-Match", created.CaseVersion.ToString());
        await client.SendAsync(request);

        var timelineResponse = await client.GetAsync($"/v1/cases/{created.CaseId}/timeline");
        var entries = await timelineResponse.Content.ReadFromJsonAsync<List<TimelineEntryResponse>>(JsonOptions);

        Assert.Equal(HttpStatusCode.OK, timelineResponse.StatusCode);
        Assert.Equal(2, entries!.Count);
        Assert.True(entries[0].CaseVersion < entries[1].CaseVersion);
        Assert.Equal(EventTypes.CaseCreated, entries[0].EventType);
        Assert.Equal("CaseCancelled", entries[1].EventType);
    }

    [Fact]
    public async Task ListCases_MissingTenantHeader_ReturnsBadRequest()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/v1/cases");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
