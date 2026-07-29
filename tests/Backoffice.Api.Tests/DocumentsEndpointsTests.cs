using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Backoffice.Application.Cases;
using Backoffice.Application.Documents;
using Backoffice.Domain.Cases;
using Backoffice.Domain.Documents;
using Microsoft.Extensions.DependencyInjection;

namespace Backoffice.Api.Tests;

public class DocumentsEndpointsTests(BackofficeApiFactory factory) : IClassFixture<BackofficeApiFactory>
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
        client.DefaultRequestHeaders.Add(RequestContext.RolesHeader, "case-manager,document-processor,auditor");
        return client;
    }

    private static async Task<CaseResponse> CreateCaseAsync(HttpClient client, string externalReference, DisputeType disputeType = DisputeType.CardPurchase)
    {
        var request = new CreateCaseRequest(externalReference, disputeType, Channel.App, Priority.Normal, new MoneyDto("BRL", "150.00"));
        var response = await client.PostAsJsonAsync("/v1/cases", request, JsonOptions);
        return (await response.Content.ReadFromJsonAsync<CaseResponse>(JsonOptions))!;
    }

    private static Task<HttpResponseMessage> RegisterDocumentAsync(
        HttpClient client, Guid caseId, long expectedVersion, DocumentType documentType, MediaType mediaType, string fileName)
    {
        var httpRequest = DocumentUploadTestHelper.BuildRequest(caseId, expectedVersion, documentType, mediaType, fileName);
        return client.SendAsync(httpRequest);
    }

    [Fact]
    public async Task RegisterDocument_CleanAndRecognized_ValidatesDocumentAndAdvancesCase()
    {
        var client = CreateClient("tenant-doc-clean");
        var @case = await CreateCaseAsync(client, "ext-doc-clean-1", DisputeType.CardPurchase);

        var response = await RegisterDocumentAsync(client, @case.CaseId, @case.CaseVersion,
            DocumentType.Receipt, MediaType.ApplicationPdf, "receipt-2026-07.pdf");
        var document = await response.Content.ReadFromJsonAsync<DocumentResponse>(JsonOptions);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(DocumentStatus.Validated, document!.Status);
        Assert.StartsWith("document-store://accepted/", document.StorageReference);

        var caseAfter = await (await client.GetAsync($"/v1/cases/{@case.CaseId}")).Content.ReadFromJsonAsync<CaseResponse>(JsonOptions);
        Assert.Equal(CaseState.DocumentsValidated, caseAfter!.State);

        var evidence = await (await client.GetAsync($"/v1/cases/{@case.CaseId}/evidence")).Content
            .ReadFromJsonAsync<List<EvidenceResponse>>(JsonOptions);
        Assert.Single(evidence!);
        Assert.Equal(document.DocumentId.ToString(), evidence![0].SourceReference);
        Assert.Equal(document.Version.ToString(), evidence[0].SourceVersion);
        Assert.True(evidence[0].Confidence > 0.5);
    }

    [Fact]
    public async Task RegisterDocument_AsyncMode_DurablyAcceptsIntoQuarantineBeforeReturning()
    {
        var asyncFactory = new BackofficeApiFactory { InlineDocumentProcessing = false };
        await asyncFactory.InitializeAsync();

        try
        {
            var client = asyncFactory.CreateClient();
            client.DefaultRequestHeaders.Add(RequestContext.TenantHeader, "tenant-doc-async");
            client.DefaultRequestHeaders.Add(RequestContext.SubjectHeader, "test-actor");
            client.DefaultRequestHeaders.Add(RequestContext.RolesHeader, "case-manager,document-processor,auditor");
            var @case = await CreateCaseAsync(client, "ext-doc-async-1");
            var fileBytes = "durably quarantined test content"u8.ToArray();
            var request = DocumentUploadTestHelper.BuildRequest(
                @case.CaseId,
                @case.CaseVersion,
                DocumentType.Receipt,
                MediaType.ApplicationPdf,
                "../receipt.pdf",
                fileBytes);

            var response = await client.SendAsync(request);
            var document = await response.Content.ReadFromJsonAsync<DocumentResponse>(JsonOptions);

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            Assert.Equal(DocumentStatus.Quarantined, document!.Status);
            Assert.StartsWith("document-store://quarantine/", document.StorageReference);
            Assert.DoesNotContain("tenant-doc-async", document.StorageReference, StringComparison.Ordinal);
            Assert.DoesNotContain("..", document.StorageReference, StringComparison.Ordinal);

            using var scope = asyncFactory.Services.CreateScope();
            var storage = scope.ServiceProvider.GetRequiredService<IDocumentStorage>();
            var storedDocument = await storage.ReadAsync(document.StorageReference);
            Assert.Equal(fileBytes, storedDocument.Content);
            Assert.Equal("receipt.pdf", storedDocument.FileName);
        }
        finally
        {
            await ((IAsyncLifetime)asyncFactory).DisposeAsync();
        }
    }

    [Fact]
    public async Task RegisterDocument_OverUploadLimit_ReturnsPayloadTooLarge()
    {
        var client = CreateClient("tenant-doc-too-large");
        var @case = await CreateCaseAsync(client, "ext-doc-too-large-1");
        var request = DocumentUploadTestHelper.BuildRequest(
            @case.CaseId,
            @case.CaseVersion,
            DocumentType.Receipt,
            MediaType.ApplicationPdf,
            "oversized.pdf",
            new byte[10 * 1024 * 1024 + 1]);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task RegisterDocument_MalwareFlagged_RejectsDocumentButStillAcceptsCase()
    {
        var client = CreateClient("tenant-doc-malware");
        var @case = await CreateCaseAsync(client, "ext-doc-malware-1");

        var response = await RegisterDocumentAsync(client, @case.CaseId, @case.CaseVersion,
            DocumentType.Receipt, MediaType.ApplicationPdf, "eicar-test-file.pdf");
        var document = await response.Content.ReadFromJsonAsync<DocumentResponse>(JsonOptions);

        Assert.Equal(DocumentStatus.Rejected, document!.Status);
        Assert.NotEmpty(document.RejectionReasons);

        var caseAfter = await (await client.GetAsync($"/v1/cases/{@case.CaseId}")).Content.ReadFromJsonAsync<CaseResponse>(JsonOptions);
        Assert.Equal(CaseState.DocumentsReceived, caseAfter!.State);
    }

    [Fact]
    public async Task RegisterDocument_UnrecognizedFilename_RequiresReviewAndDoesNotAdvanceCase()
    {
        var client = CreateClient("tenant-doc-unknown");
        var @case = await CreateCaseAsync(client, "ext-doc-unknown-1");

        var response = await RegisterDocumentAsync(client, @case.CaseId, @case.CaseVersion,
            DocumentType.Receipt, MediaType.ApplicationPdf, "file-20260727-0001.pdf");
        var document = await response.Content.ReadFromJsonAsync<DocumentResponse>(JsonOptions);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(DocumentStatus.ReviewRequired, document!.Status);

        var caseAfter = await (await client.GetAsync($"/v1/cases/{@case.CaseId}")).Content
            .ReadFromJsonAsync<CaseResponse>(JsonOptions);
        Assert.Equal(CaseState.DocumentsReceived, caseAfter!.State);

        var evidence = await (await client.GetAsync($"/v1/cases/{@case.CaseId}/evidence")).Content
            .ReadFromJsonAsync<List<EvidenceResponse>>(JsonOptions);
        Assert.Empty(evidence!);
    }

    [Fact]
    public async Task RegisterDocument_ClassificationMismatch_RequiresReviewAndDoesNotCreateEvidence()
    {
        var client = CreateClient("tenant-doc-mismatch");
        var @case = await CreateCaseAsync(client, "ext-doc-mismatch-1");

        var response = await RegisterDocumentAsync(client, @case.CaseId, @case.CaseVersion,
            DocumentType.Receipt, MediaType.ApplicationXlsx, "statement-july-2026.xlsx");
        var document = await response.Content.ReadFromJsonAsync<DocumentResponse>(JsonOptions);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(DocumentStatus.ReviewRequired, document!.Status);

        var caseAfter = await (await client.GetAsync($"/v1/cases/{@case.CaseId}")).Content
            .ReadFromJsonAsync<CaseResponse>(JsonOptions);
        Assert.Equal(CaseState.DocumentsReceived, caseAfter!.State);

        var evidence = await (await client.GetAsync($"/v1/cases/{@case.CaseId}/evidence")).Content
            .ReadFromJsonAsync<List<EvidenceResponse>>(JsonOptions);
        Assert.Empty(evidence!);
    }

    [Fact]
    public async Task GetDocument_CrossTenant_ReturnsNotFound()
    {
        var ownerClient = CreateClient("tenant-doc-owner");
        var @case = await CreateCaseAsync(ownerClient, "ext-doc-owner-1");
        var registerResponse = await RegisterDocumentAsync(ownerClient, @case.CaseId, @case.CaseVersion,
            DocumentType.Receipt, MediaType.ApplicationPdf, "receipt-owner.pdf");
        var document = await registerResponse.Content.ReadFromJsonAsync<DocumentResponse>(JsonOptions);

        var otherClient = CreateClient("tenant-doc-other");
        var response = await otherClient.GetAsync($"/v1/cases/{@case.CaseId}/documents/{document!.DocumentId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
