using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Backoffice.Application.Approvals;
using Backoffice.Application.Cases;
using Backoffice.Application.Documents;
using Backoffice.Application.Investigations;
using Backoffice.Application.Recommendations;
using Backoffice.Domain.Approvals;
using Backoffice.Domain.Cases;
using Backoffice.Domain.Documents;
using Microsoft.Extensions.DependencyInjection;

namespace Backoffice.Api.Tests;

public class HumanApprovalTests(BackofficeApiFactory factory) : IClassFixture<BackofficeApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(ScreamingSnakeCaseNamingPolicy.Instance) },
    };

    private HttpClient CreateClient(string tenantId, string actorId = "recommender-actor")
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(RequestContext.TenantHeader, tenantId);
        client.DefaultRequestHeaders.Add(RequestContext.SubjectHeader, actorId);
        // Roles cover both halves of the recommend-then-approve workflow this fixture drives;
        // segregation-of-duties/alçada are actor-identity and authority-limit checks, not
        // role-based, so granting every client the same roles doesn't weaken those scenarios.
        client.DefaultRequestHeaders.Add(RequestContext.RolesHeader, "case-manager,operations-analyst,approver");
        return client;
    }

    private static async Task<CaseResponse> CreateCaseAsync(HttpClient client, string externalReference)
    {
        var request = new CreateCaseRequest(externalReference, DisputeType.CardPurchase, Channel.App, Priority.Normal, new MoneyDto("BRL", "150.00"));
        var response = await client.PostAsJsonAsync("/v1/cases", request, JsonOptions);
        return (await response.Content.ReadFromJsonAsync<CaseResponse>(JsonOptions))!;
    }

    /// <summary>Drives a fresh case all the way to AWAITING_APPROVAL with a grounded APPROVE recommendation.</summary>
    private static async Task<(CaseResponse Case, Guid RecommendationId, long RecommendationVersion)> BringToAwaitingApprovalAsync(
        HttpClient client, string externalReference)
    {
        var @case = await CreateCaseAsync(client, externalReference);

        var docRequest = DocumentUploadTestHelper.BuildRequest(@case.CaseId, @case.CaseVersion, DocumentType.Receipt, MediaType.ApplicationPdf, "receipt-2026.pdf");
        await client.SendAsync(docRequest);
        var caseAfterDoc = await (await client.GetAsync($"/v1/cases/{@case.CaseId}")).Content.ReadFromJsonAsync<CaseResponse>(JsonOptions);

        var invRequest = new HttpRequestMessage(HttpMethod.Post, $"/v1/cases/{@case.CaseId}/investigations")
        {
            Content = JsonContent.Create(new StartInvestigationRequest([RequestedCheck.TransactionLookup]), options: JsonOptions),
        };
        invRequest.Headers.TryAddWithoutValidation("If-Match", caseAfterDoc!.CaseVersion.ToString());
        var invResponse = await client.SendAsync(invRequest);
        var investigation = await invResponse.Content.ReadFromJsonAsync<InvestigationResponse>(JsonOptions);
        var caseAfterInvestigation = await (await client.GetAsync($"/v1/cases/{@case.CaseId}")).Content.ReadFromJsonAsync<CaseResponse>(JsonOptions);

        var recResponse = await client.PostAsJsonAsync(
            $"/v1/cases/{@case.CaseId}/recommendations",
            new CreateRecommendationRequest(caseAfterInvestigation!.CaseVersion, investigation!.InvestigationId),
            JsonOptions);
        var recommendation = await recResponse.Content.ReadFromJsonAsync<RecommendationResponse>(JsonOptions);

        var caseAfterRecommendation = await (await client.GetAsync($"/v1/cases/{@case.CaseId}")).Content
            .ReadFromJsonAsync<CaseResponse>(JsonOptions);

        return (caseAfterRecommendation!, recommendation!.RecommendationId, recommendation.RecommendationVersion);
    }

    private static Task<HttpResponseMessage> DecideApprovalAsync(
        HttpClient client, Guid caseId, long caseVersion, Guid recommendationId, long recommendationVersion,
        ApprovalDecision decision, string? authorityLimit = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/cases/{caseId}/approvals")
        {
            Content = JsonContent.Create(
                new DecideApprovalRequest(caseVersion, recommendationId, recommendationVersion, decision, "test reason", null),
                options: JsonOptions),
        };
        if (authorityLimit is not null)
        {
            request.Headers.TryAddWithoutValidation(RequestContext.AuthorityLimitHeader, authorityLimit);
        }
        return client.SendAsync(request);
    }

    [Fact]
    public async Task DecideApproval_BySameActorAsRecommendation_ReturnsForbidden()
    {
        var client = CreateClient("tenant-approval-self", "same-actor");
        var (@case, recommendationId, recommendationVersion) = await BringToAwaitingApprovalAsync(client, "ext-approval-self-1");

        // Same client/actor authored the recommendation, then tries to approve it.
        var response = await DecideApprovalAsync(
            client, @case.CaseId, @case.CaseVersion, recommendationId, recommendationVersion, ApprovalDecision.Approve);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DecideApproval_OverAuthorityLimit_ReturnsForbidden()
    {
        var recommenderClient = CreateClient("tenant-approval-limit", "recommender-1");
        var (@case, recommendationId, recommendationVersion) = await BringToAwaitingApprovalAsync(recommenderClient, "ext-approval-limit-1");

        var approverClient = CreateClient("tenant-approval-limit", "approver-1");
        var response = await DecideApprovalAsync(
            approverClient, @case.CaseId, @case.CaseVersion, recommendationId, recommendationVersion,
            ApprovalDecision.Approve, authorityLimit: "50.00");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DecideApproval_StaleRecommendationVersion_ReturnsConflict()
    {
        var recommenderClient = CreateClient("tenant-approval-stale", "recommender-2");
        var (@case, recommendationId, recommendationVersion) = await BringToAwaitingApprovalAsync(recommenderClient, "ext-approval-stale-1");

        var approverClient = CreateClient("tenant-approval-stale", "approver-2");
        var staleVersion = recommendationVersion + 1;
        var response = await DecideApprovalAsync(
            approverClient, @case.CaseId, @case.CaseVersion, recommendationId, staleVersion,
            ApprovalDecision.Approve, authorityLimit: "999999.00");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task DecideApproval_ApproveWithSufficientAuthority_TransitionsCaseToApproved()
    {
        var recommenderClient = CreateClient("tenant-approval-ok", "recommender-3");
        var (@case, recommendationId, recommendationVersion) = await BringToAwaitingApprovalAsync(recommenderClient, "ext-approval-ok-1");

        var approverClient = CreateClient("tenant-approval-ok", "approver-3");
        var response = await DecideApprovalAsync(
            approverClient, @case.CaseId, @case.CaseVersion, recommendationId, recommendationVersion,
            ApprovalDecision.Approve, authorityLimit: "999999.00");
        var approval = await response.Content.ReadFromJsonAsync<ApprovalResponse>(JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(ApprovalStatus.Approved, approval!.Status);
        Assert.NotNull(approval.ExpiresAt);

        var caseAfter = await (await approverClient.GetAsync($"/v1/cases/{@case.CaseId}")).Content
            .ReadFromJsonAsync<CaseResponse>(JsonOptions);
        Assert.Equal(CaseState.Approved, caseAfter!.State);
    }

    [Fact]
    public async Task GetCase_PastApprovalDeadline_ExpiresCaseOnRead()
    {
        var fakeClock = new FakeClock();
        using var expiryFactory = new BackofficeApiFactory
        {
            ConfigureTestServices = services =>
                services.AddSingleton<Backoffice.Application.Abstractions.IClock>(fakeClock),
        };
        // Manually constructed (not fixture-injected), so xUnit never calls IAsyncLifetime
        // on it automatically — start the OPA subprocess explicitly before first use.
        await ((IAsyncLifetime)expiryFactory).InitializeAsync();

        var client = expiryFactory.CreateClient();
        client.DefaultRequestHeaders.Add(RequestContext.TenantHeader, "tenant-approval-expiry");
        client.DefaultRequestHeaders.Add(RequestContext.SubjectHeader, "recommender-expiry");
        client.DefaultRequestHeaders.Add(RequestContext.RolesHeader, "case-manager,operations-analyst,approver");

        var (@case, _, _) = await BringToAwaitingApprovalAsync(client, "ext-approval-expiry-1");
        Assert.Equal(CaseState.AwaitingApproval, @case.State);

        fakeClock.UtcNow = fakeClock.UtcNow.Add(Backoffice.Domain.Cases.Case.ApprovalWindow).AddMinutes(1);

        var caseAfterExpiry = await (await client.GetAsync($"/v1/cases/{@case.CaseId}")).Content
            .ReadFromJsonAsync<CaseResponse>(JsonOptions);

        Assert.Equal(CaseState.Expired, caseAfterExpiry!.State);
    }
}
