using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Backoffice.Application.Approvals;
using Backoffice.Application.Cases;
using Backoffice.Application.Documents;
using Backoffice.Application.Investigations;
using Backoffice.Application.Recommendations;
using Backoffice.Domain.Cases;
using Backoffice.Domain.Documents;

namespace Backoffice.Api.Tests;

public class WorkflowResourceQueryTests(BackofficeApiFactory factory) : IClassFixture<BackofficeApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(ScreamingSnakeCaseNamingPolicy.Instance) },
    };

    [Fact]
    public async Task ListWorkflowResources_ReturnsPersistedHistoryWithoutCrossTenantLeakage()
    {
        var tenantId = "tenant-workflow-history";
        var recommenderClient = CreateClient(tenantId, "recommender-history");
        var (@case, recommendation) = await BringToAwaitingApprovalAsync(
            recommenderClient, "ext-workflow-history-1");

        var recommendationsResponse = await recommenderClient.GetAsync($"/v1/cases/{@case.CaseId}/recommendations");
        var recommendations = await recommendationsResponse.Content
            .ReadFromJsonAsync<List<RecommendationResponse>>(JsonOptions);
        var approvalsBeforeResponse = await recommenderClient.GetAsync($"/v1/cases/{@case.CaseId}/approvals");
        var approvalsBefore = await approvalsBeforeResponse.Content
            .ReadFromJsonAsync<List<ApprovalResponse>>(JsonOptions);

        Assert.Equal(HttpStatusCode.OK, recommendationsResponse.StatusCode);
        Assert.Single(recommendations!);
        Assert.Equal(recommendation.RecommendationId, recommendations[0].RecommendationId);
        Assert.Equal(recommendation.RecommendationVersion, recommendations[0].RecommendationVersion);
        Assert.Equal(HttpStatusCode.OK, approvalsBeforeResponse.StatusCode);
        Assert.Empty(approvalsBefore!);

        var approverClient = CreateClient(tenantId, "approver-history");
        var decisionResponse = await DecideApprovalAsync(approverClient, @case, recommendation);
        var decision = await decisionResponse.Content.ReadFromJsonAsync<ApprovalResponse>(JsonOptions);

        var approvalsAfterResponse = await approverClient.GetAsync($"/v1/cases/{@case.CaseId}/approvals");
        var approvalsAfter = await approvalsAfterResponse.Content
            .ReadFromJsonAsync<List<ApprovalResponse>>(JsonOptions);

        Assert.Equal(HttpStatusCode.Created, decisionResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, approvalsAfterResponse.StatusCode);
        Assert.Single(approvalsAfter!);
        Assert.Equal(decision!.ApprovalId, approvalsAfter[0].ApprovalId);

        var foreignTenantClient = CreateClient("tenant-workflow-history-other", "reader-history");
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await foreignTenantClient.GetAsync($"/v1/cases/{@case.CaseId}/recommendations")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await foreignTenantClient.GetAsync($"/v1/cases/{@case.CaseId}/approvals")).StatusCode);
    }

    private HttpClient CreateClient(string tenantId, string actorId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(RequestContext.TenantHeader, tenantId);
        client.DefaultRequestHeaders.Add(RequestContext.SubjectHeader, actorId);
        client.DefaultRequestHeaders.Add(RequestContext.RolesHeader, "case-manager,operations-analyst,approver");
        return client;
    }

    private static async Task<(CaseResponse Case, RecommendationResponse Recommendation)> BringToAwaitingApprovalAsync(
        HttpClient client,
        string externalReference)
    {
        var createResponse = await client.PostAsJsonAsync(
            "/v1/cases",
            new CreateCaseRequest(
                externalReference,
                DisputeType.CardPurchase,
                Channel.App,
                Priority.Normal,
                new MoneyDto("BRL", "150.00")),
            JsonOptions);
        var @case = (await createResponse.Content.ReadFromJsonAsync<CaseResponse>(JsonOptions))!;

        var documentRequest = DocumentUploadTestHelper.BuildRequest(
            @case.CaseId,
            @case.CaseVersion,
            DocumentType.Receipt,
            MediaType.ApplicationPdf,
            "receipt-2026.pdf");
        await client.SendAsync(documentRequest);

        var caseAfterDocument = await (await client.GetAsync($"/v1/cases/{@case.CaseId}")).Content
            .ReadFromJsonAsync<CaseResponse>(JsonOptions);
        var investigationRequest = new HttpRequestMessage(HttpMethod.Post, $"/v1/cases/{@case.CaseId}/investigations")
        {
            Content = JsonContent.Create(
                new StartInvestigationRequest([RequestedCheck.TransactionLookup]),
                options: JsonOptions),
        };
        investigationRequest.Headers.TryAddWithoutValidation("If-Match", caseAfterDocument!.CaseVersion.ToString());
        var investigationResponse = await client.SendAsync(investigationRequest);
        var investigation = await investigationResponse.Content.ReadFromJsonAsync<InvestigationResponse>(JsonOptions);

        var caseAfterInvestigation = await (await client.GetAsync($"/v1/cases/{@case.CaseId}")).Content
            .ReadFromJsonAsync<CaseResponse>(JsonOptions);
        var recommendationResponse = await client.PostAsJsonAsync(
            $"/v1/cases/{@case.CaseId}/recommendations",
            new CreateRecommendationRequest(caseAfterInvestigation!.CaseVersion, investigation!.InvestigationId),
            JsonOptions);
        var recommendation = (await recommendationResponse.Content
            .ReadFromJsonAsync<RecommendationResponse>(JsonOptions))!;
        var caseAfterRecommendation = await (await client.GetAsync($"/v1/cases/{@case.CaseId}")).Content
            .ReadFromJsonAsync<CaseResponse>(JsonOptions);

        return (caseAfterRecommendation!, recommendation);
    }

    private static Task<HttpResponseMessage> DecideApprovalAsync(
        HttpClient client,
        CaseResponse @case,
        RecommendationResponse recommendation)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/cases/{@case.CaseId}/approvals")
        {
            Content = JsonContent.Create(
                new DecideApprovalRequest(
                    @case.CaseVersion,
                    recommendation.RecommendationId,
                    recommendation.RecommendationVersion,
                    ApprovalDecision.Approve,
                    "approved by integration test",
                    null),
                options: JsonOptions),
        };
        request.Headers.TryAddWithoutValidation(RequestContext.AuthorityLimitHeader, "999999.00");
        return client.SendAsync(request);
    }
}
