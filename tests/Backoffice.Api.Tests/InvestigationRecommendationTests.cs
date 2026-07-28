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
        client.DefaultRequestHeaders.Add(RequestContext.RolesHeader, "case-manager,operations-analyst,auditor");
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
    public async Task StartInvestigation_NoEvidence_IsRejectedByPolicy()
    {
        var client = CreateClient("tenant-inv-empty");
        var @case = await CreateCaseAsync(client, "ext-inv-empty-1");

        // Unrecognized filename: document validates (satisfies the Receipt requirement) but
        // the classifier abstains, so no Evidence record is created for it.
        var caseAfterDoc = await RegisterDocumentToValidatedAsync(client, @case.CaseId, @case.CaseVersion, "file-0001.pdf");
        Assert.Equal(CaseState.DocumentsValidated, caseAfterDoc.State);

        // policies/authorization.rego's investigation.execute rule requires evidence_present,
        // so a case with zero evidence is denied (403) before InvestigationEngine ever runs.
        // The MissingData-finding path for zero-evidence input is still covered directly
        // against InvestigationEngine by Backoffice.Evals, bypassing HTTP/OPA.
        var response = await StartInvestigationAsync(client, @case.CaseId, caseAfterDoc.CaseVersion);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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

        // CreateRecommendationHandler's own state pre-check (matching
        // policies/authorization.rego's recommendation.create rule, which requires
        // resource.state == UNDER_INVESTIGATION exactly) now rejects a superseding
        // recommendation while AWAITING_APPROVAL as an invalid transition, before OPA is
        // even consulted — previously this handler allowed it to test version-incrementing.
        var secondResponse = await CreateRecommendationAsync(
            client, @case.CaseId, caseAfterRecommendation.CaseVersion, investigation.InvestigationId);

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
    }
}
