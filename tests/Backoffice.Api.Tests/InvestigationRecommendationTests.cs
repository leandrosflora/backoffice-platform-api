using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Backoffice.Application.Cases;
using Backoffice.Application.Documents;
using Backoffice.Application.Investigations;
using Backoffice.Application.Recommendations;
using Backoffice.Domain.Cases;
using Backoffice.Domain.Documents;
using Backoffice.Domain.Investigations;
using Backoffice.Domain.Recommendations;

namespace Backoffice.Api.Tests;

public class InvestigationRecommendationTests(BackofficeApiFactory factory) : IClassFixture<BackofficeApiFactory>
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
        return client;
    }

    private static async Task<CaseResponse> CreateCaseAsync(HttpClient client, string externalReference)
    {
        var request = new CreateCaseRequest(externalReference, DisputeType.CardPurchase, Channel.App, Priority.Normal, new MoneyDto("BRL", "150.00"));
        var response = await client.PostAsJsonAsync("/v1/cases", request, JsonOptions);
        return (await response.Content.ReadFromJsonAsync<CaseResponse>(JsonOptions))!;
    }

    private static async Task<CaseResponse> RegisterDocumentToValidatedAsync(
        HttpClient client, Guid caseId, long expectedVersion, string storageReference)
    {
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"/v1/cases/{caseId}/documents")
        {
            Content = JsonContent.Create(
                new RegisterDocumentRequest(DocumentType.Receipt, MediaType.ApplicationPdf, new string('a', 64), storageReference),
                options: JsonOptions),
        };
        httpRequest.Headers.TryAddWithoutValidation("If-Match", expectedVersion.ToString());
        await client.SendAsync(httpRequest);

        return (await (await client.GetAsync($"/v1/cases/{caseId}")).Content.ReadFromJsonAsync<CaseResponse>(JsonOptions))!;
    }

    private static Task<HttpResponseMessage> StartInvestigationAsync(HttpClient client, Guid caseId, long expectedVersion)
    {
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"/v1/cases/{caseId}/investigations")
        {
            Content = JsonContent.Create(new StartInvestigationRequest([RequestedCheck.TransactionLookup]), options: JsonOptions),
        };
        httpRequest.Headers.TryAddWithoutValidation("If-Match", expectedVersion.ToString());
        return client.SendAsync(httpRequest);
    }

    private static Task<HttpResponseMessage> CreateRecommendationAsync(HttpClient client, Guid caseId, long caseVersion, Guid investigationId) =>
        client.PostAsJsonAsync(
            $"/v1/cases/{caseId}/recommendations", new CreateRecommendationRequest(caseVersion, investigationId), JsonOptions);

    [Fact]
    public async Task StartInvestigation_NoEvidence_ProducesMissingDataFinding()
    {
        var client = CreateClient("tenant-inv-empty");
        var @case = await CreateCaseAsync(client, "ext-inv-empty-1");

        // Unrecognized filename: document validates (satisfies the Receipt requirement) but
        // the classifier abstains, so no Evidence record is created for it.
        var caseAfterDoc = await RegisterDocumentToValidatedAsync(client, @case.CaseId, @case.CaseVersion, "file-0001.pdf");
        Assert.Equal(CaseState.DocumentsValidated, caseAfterDoc.State);

        var response = await StartInvestigationAsync(client, @case.CaseId, caseAfterDoc.CaseVersion);
        var investigation = await response.Content.ReadFromJsonAsync<InvestigationResponse>(JsonOptions);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Single(investigation!.Findings);
        Assert.Equal(FindingKind.MissingData, investigation.Findings[0].Kind);
    }

    [Fact]
    public async Task StartInvestigation_WithEvidence_ProducesConfirmedFact()
    {
        var client = CreateClient("tenant-inv-evidence");
        var @case = await CreateCaseAsync(client, "ext-inv-evidence-1");
        var caseAfterDoc = await RegisterDocumentToValidatedAsync(client, @case.CaseId, @case.CaseVersion, "receipt-2026.pdf");

        var response = await StartInvestigationAsync(client, @case.CaseId, caseAfterDoc.CaseVersion);
        var investigation = await response.Content.ReadFromJsonAsync<InvestigationResponse>(JsonOptions);

        Assert.Single(investigation!.Findings);
        Assert.Equal(FindingKind.ConfirmedFact, investigation.Findings[0].Kind);
        Assert.NotEmpty(investigation.Findings[0].EvidenceReferences);
    }

    [Fact]
    public async Task CreateRecommendation_NoEvidenceAtAll_ReturnsUnprocessableEntity()
    {
        var client = CreateClient("tenant-rec-noevidence");
        var @case = await CreateCaseAsync(client, "ext-rec-noevidence-1");
        var caseAfterDoc = await RegisterDocumentToValidatedAsync(client, @case.CaseId, @case.CaseVersion, "file-0002.pdf");

        var investigationResponse = await StartInvestigationAsync(client, @case.CaseId, caseAfterDoc.CaseVersion);
        var investigation = await investigationResponse.Content.ReadFromJsonAsync<InvestigationResponse>(JsonOptions);

        var caseAfterInvestigation = await (await client.GetAsync($"/v1/cases/{@case.CaseId}")).Content
            .ReadFromJsonAsync<CaseResponse>(JsonOptions);

        var response = await CreateRecommendationAsync(client, @case.CaseId, caseAfterInvestigation!.CaseVersion, investigation!.InvestigationId);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task CreateRecommendation_Grounded_ApprovesAndAdvancesCaseToAwaitingApproval()
    {
        var client = CreateClient("tenant-rec-grounded");
        var @case = await CreateCaseAsync(client, "ext-rec-grounded-1");
        var caseAfterDoc = await RegisterDocumentToValidatedAsync(client, @case.CaseId, @case.CaseVersion, "receipt-2026.pdf");

        var investigationResponse = await StartInvestigationAsync(client, @case.CaseId, caseAfterDoc.CaseVersion);
        var investigation = await investigationResponse.Content.ReadFromJsonAsync<InvestigationResponse>(JsonOptions);
        var caseAfterInvestigation = await (await client.GetAsync($"/v1/cases/{@case.CaseId}")).Content
            .ReadFromJsonAsync<CaseResponse>(JsonOptions);

        var response = await CreateRecommendationAsync(client, @case.CaseId, caseAfterInvestigation!.CaseVersion, investigation!.InvestigationId);
        var recommendation = await response.Content.ReadFromJsonAsync<RecommendationResponse>(JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(RecommendationOutcome.Approve, recommendation!.Outcome);
        Assert.Equal(1, recommendation.RecommendationVersion);
        Assert.NotEmpty(recommendation.EvidenceReferences);
        Assert.NotEmpty(recommendation.RuleReferences);

        var caseAfterRecommendation = await (await client.GetAsync($"/v1/cases/{@case.CaseId}")).Content
            .ReadFromJsonAsync<CaseResponse>(JsonOptions);
        Assert.Equal(CaseState.AwaitingApproval, caseAfterRecommendation!.State);

        // Calling again (e.g. a superseding recommendation before approval) increments the version.
        var secondResponse = await CreateRecommendationAsync(
            client, @case.CaseId, caseAfterRecommendation.CaseVersion, investigation.InvestigationId);
        var secondRecommendation = await secondResponse.Content.ReadFromJsonAsync<RecommendationResponse>(JsonOptions);

        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        Assert.Equal(2, secondRecommendation!.RecommendationVersion);
    }
}
