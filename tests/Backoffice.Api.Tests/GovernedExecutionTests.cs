using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Backoffice.Application.Approvals;
using Backoffice.Application.Cases;
using Backoffice.Application.Documents;
using Backoffice.Application.Executions;
using Backoffice.Application.Investigations;
using Backoffice.Application.Recommendations;
using Backoffice.Domain.Cases;
using Backoffice.Domain.Documents;
using Backoffice.Domain.Executions;

namespace Backoffice.Api.Tests;

public class GovernedExecutionTests(BackofficeApiFactory factory) : IClassFixture<BackofficeApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(ScreamingSnakeCaseNamingPolicy.Instance) },
    };

    private HttpClient CreateClient(string tenantId, string actorId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(RequestContext.TenantHeader, tenantId);
        client.DefaultRequestHeaders.Add(RequestContext.SubjectHeader, actorId);
        // Covers case.create/document.register (case-manager, operations-analyst),
        // approval.decide/case.read (approver), and reconciliation.resolve (reconciler).
        client.DefaultRequestHeaders.Add(RequestContext.RolesHeader, "case-manager,operations-analyst,approver,reconciler");
        return client;
    }

    // execution.request requires subject.type == WORKLOAD and role execution-service
    // (policies/authorization.rego) — distinct from the HUMAN approver identity above,
    // since a single client can't satisfy both subject.type constraints at once.
    private HttpClient CreateExecutionClient(string tenantId, string actorId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(RequestContext.TenantHeader, tenantId);
        client.DefaultRequestHeaders.Add(RequestContext.SubjectHeader, actorId);
        client.DefaultRequestHeaders.Add(RequestContext.RolesHeader, "execution-service");
        client.DefaultRequestHeaders.Add(RequestContext.SubjectTypeHeader, "WORKLOAD");
        return client;
    }

    /// <summary>Drives a fresh case all the way to APPROVED, returning the approved case, the approval id, the approved recommendation version, and evidence ids to cite.</summary>
    private static async Task<(CaseResponse Case, Guid ApprovalId, long RecommendationVersion, IReadOnlyList<Guid> EvidenceIds)> BringToApprovedAsync(
        HttpClient recommenderClient, HttpClient approverClient, string tenantId, string externalReference)
    {
        var caseRequest = new CreateCaseRequest(externalReference, DisputeType.CardPurchase, Channel.App, Priority.Normal, new MoneyDto("BRL", "150.00"));
        var caseResp = await recommenderClient.PostAsJsonAsync("/v1/cases", caseRequest, JsonOptions);
        var @case = await caseResp.Content.ReadFromJsonAsync<CaseResponse>(JsonOptions);

        var docReq = new HttpRequestMessage(HttpMethod.Post, $"/v1/cases/{@case!.CaseId}/documents")
        {
            Content = JsonContent.Create(new RegisterDocumentRequest(DocumentType.Receipt, MediaType.ApplicationPdf, new string('a', 64), "receipt-2026.pdf"), options: JsonOptions),
        };
        docReq.Headers.TryAddWithoutValidation("If-Match", @case.CaseVersion.ToString());
        await recommenderClient.SendAsync(docReq);
        var caseAfterDoc = await (await recommenderClient.GetAsync($"/v1/cases/{@case.CaseId}")).Content.ReadFromJsonAsync<CaseResponse>(JsonOptions);

        var evidence = await (await recommenderClient.GetAsync($"/v1/cases/{@case.CaseId}/evidence")).Content
            .ReadFromJsonAsync<List<EvidenceResponse>>(JsonOptions);
        var evidenceIds = evidence!.Select(e => e.EvidenceId).ToList();

        var invReq = new HttpRequestMessage(HttpMethod.Post, $"/v1/cases/{@case.CaseId}/investigations")
        {
            Content = JsonContent.Create(new StartInvestigationRequest([RequestedCheck.TransactionLookup]), options: JsonOptions),
        };
        invReq.Headers.TryAddWithoutValidation("If-Match", caseAfterDoc!.CaseVersion.ToString());
        var invResp = await recommenderClient.SendAsync(invReq);
        var investigation = await invResp.Content.ReadFromJsonAsync<InvestigationResponse>(JsonOptions);
        var caseAfterInv = await (await recommenderClient.GetAsync($"/v1/cases/{@case.CaseId}")).Content.ReadFromJsonAsync<CaseResponse>(JsonOptions);

        var recResp = await recommenderClient.PostAsJsonAsync(
            $"/v1/cases/{@case.CaseId}/recommendations",
            new CreateRecommendationRequest(caseAfterInv!.CaseVersion, investigation!.InvestigationId),
            JsonOptions);
        var recommendation = await recResp.Content.ReadFromJsonAsync<RecommendationResponse>(JsonOptions);
        var caseAfterRec = await (await recommenderClient.GetAsync($"/v1/cases/{@case.CaseId}")).Content.ReadFromJsonAsync<CaseResponse>(JsonOptions);

        var approvalReq = new HttpRequestMessage(HttpMethod.Post, $"/v1/cases/{@case.CaseId}/approvals")
        {
            Content = JsonContent.Create(
                new DecideApprovalRequest(caseAfterRec!.CaseVersion, recommendation!.RecommendationId, recommendation.RecommendationVersion, ApprovalDecision.Approve, "approved", null),
                options: JsonOptions),
        };
        approvalReq.Headers.TryAddWithoutValidation(RequestContext.AuthorityLimitHeader, "999999.00");
        var approvalResp = await approverClient.SendAsync(approvalReq);
        var approval = await approvalResp.Content.ReadFromJsonAsync<ApprovalResponse>(JsonOptions);

        var caseAfterApproval = await (await recommenderClient.GetAsync($"/v1/cases/{@case.CaseId}")).Content
            .ReadFromJsonAsync<CaseResponse>(JsonOptions);

        return (caseAfterApproval!, approval!.ApprovalId, recommendation.RecommendationVersion, evidenceIds);
    }

    private static Task<HttpResponseMessage> RequestExecutionAsync(
        HttpClient client, Guid caseId, long caseVersion, string idempotencyKey, RequestExecutionRequest request)
    {
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"/v1/cases/{caseId}/executions")
        {
            Content = JsonContent.Create(request, options: JsonOptions),
        };
        httpRequest.Headers.TryAddWithoutValidation("If-Match", caseVersion.ToString());
        httpRequest.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        return client.SendAsync(httpRequest);
    }

    [Fact]
    public async Task RequestExecution_Successful_TransitionsCaseToExecuted()
    {
        var recommenderClient = CreateClient("tenant-exec-success", "recommender-exec-1");
        var approverClient = CreateClient("tenant-exec-success", "approver-exec-1");
        var executionClient = CreateExecutionClient("tenant-exec-success", "execution-service-1");
        var (@case, approvalId, recommendationVersion, evidenceIds) = await BringToApprovedAsync(recommenderClient, approverClient, "tenant-exec-success", "ext-exec-success-1");

        var request = new RequestExecutionRequest(
            @case.CaseVersion, approvalId, recommendationVersion, CommandType.MockRefund, "clean-command-hash-1", evidenceIds);
        var response = await RequestExecutionAsync(executionClient, @case.CaseId, @case.CaseVersion, "idem-key-success-1", request);
        var execution = await response.Content.ReadFromJsonAsync<ExecutionResponse>(JsonOptions);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(ExecutionStatus.Succeeded, execution!.Status);
        Assert.NotNull(execution.ExternalReference);

        var caseAfter = await (await approverClient.GetAsync($"/v1/cases/{@case.CaseId}")).Content.ReadFromJsonAsync<CaseResponse>(JsonOptions);
        Assert.Equal(CaseState.Executed, caseAfter!.State);
    }

    [Fact]
    public async Task RequestExecution_IdempotentReplay_ReturnsSameExecution()
    {
        var recommenderClient = CreateClient("tenant-exec-replay", "recommender-exec-2");
        var approverClient = CreateClient("tenant-exec-replay", "approver-exec-2");
        var executionClient = CreateExecutionClient("tenant-exec-replay", "execution-service-2");
        var (@case, approvalId, recommendationVersion, evidenceIds) = await BringToApprovedAsync(recommenderClient, approverClient, "tenant-exec-replay", "ext-exec-replay-1");

        var request = new RequestExecutionRequest(
            @case.CaseVersion, approvalId, recommendationVersion, CommandType.MockRefund, "clean-command-hash-2", evidenceIds);

        var first = await RequestExecutionAsync(executionClient, @case.CaseId, @case.CaseVersion, "idem-key-replay-1", request);
        var firstExecution = await first.Content.ReadFromJsonAsync<ExecutionResponse>(JsonOptions);

        var second = await RequestExecutionAsync(executionClient, @case.CaseId, @case.CaseVersion, "idem-key-replay-1", request);
        var secondExecution = await second.Content.ReadFromJsonAsync<ExecutionResponse>(JsonOptions);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(firstExecution!.ExecutionId, secondExecution!.ExecutionId);
    }

    [Fact]
    public async Task RequestExecution_SameKeyDifferentPayload_ReturnsConflict()
    {
        var recommenderClient = CreateClient("tenant-exec-conflict", "recommender-exec-3");
        var approverClient = CreateClient("tenant-exec-conflict", "approver-exec-3");
        var executionClient = CreateExecutionClient("tenant-exec-conflict", "execution-service-3");
        var (@case, approvalId, recommendationVersion, evidenceIds) = await BringToApprovedAsync(recommenderClient, approverClient, "tenant-exec-conflict", "ext-exec-conflict-1");

        var firstRequest = new RequestExecutionRequest(
            @case.CaseVersion, approvalId, recommendationVersion, CommandType.MockRefund, "clean-command-hash-3a", evidenceIds);
        await RequestExecutionAsync(executionClient, @case.CaseId, @case.CaseVersion, "idem-key-conflict-1", firstRequest);

        var secondRequest = new RequestExecutionRequest(
            @case.CaseVersion, approvalId, recommendationVersion, CommandType.MockRefund, "clean-command-hash-3b", evidenceIds);
        var second = await RequestExecutionAsync(executionClient, @case.CaseId, @case.CaseVersion, "idem-key-conflict-1", secondRequest);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task RequestExecution_AmbiguousResult_SetsReconciliationRequired()
    {
        var recommenderClient = CreateClient("tenant-exec-ambiguous", "recommender-exec-4");
        var approverClient = CreateClient("tenant-exec-ambiguous", "approver-exec-4");
        var executionClient = CreateExecutionClient("tenant-exec-ambiguous", "execution-service-4");
        var (@case, approvalId, recommendationVersion, evidenceIds) = await BringToApprovedAsync(recommenderClient, approverClient, "tenant-exec-ambiguous", "ext-exec-ambiguous-1");

        var request = new RequestExecutionRequest(
            @case.CaseVersion, approvalId, recommendationVersion, CommandType.MockRefund, "ambiguous-command-hash-4", evidenceIds);
        var response = await RequestExecutionAsync(executionClient, @case.CaseId, @case.CaseVersion, "idem-key-ambiguous-1", request);
        var execution = await response.Content.ReadFromJsonAsync<ExecutionResponse>(JsonOptions);

        Assert.Equal(ExecutionStatus.ReconciliationRequired, execution!.Status);

        var caseAfter = await (await approverClient.GetAsync($"/v1/cases/{@case.CaseId}")).Content.ReadFromJsonAsync<CaseResponse>(JsonOptions);
        Assert.Equal(CaseState.ReconciliationRequired, caseAfter!.State);
    }

    [Fact]
    public async Task ResolveReconciliation_ConfirmedSucceeded_TransitionsCaseToExecuted()
    {
        var recommenderClient = CreateClient("tenant-recon-success", "recommender-exec-5");
        var approverClient = CreateClient("tenant-recon-success", "approver-exec-5");
        var executionClient = CreateExecutionClient("tenant-recon-success", "execution-service-5");
        var (@case, approvalId, recommendationVersion, evidenceIds) = await BringToApprovedAsync(recommenderClient, approverClient, "tenant-recon-success", "ext-recon-success-1");

        var request = new RequestExecutionRequest(
            @case.CaseVersion, approvalId, recommendationVersion, CommandType.MockRefund, "ambiguous-command-hash-5", evidenceIds);
        var execResponse = await RequestExecutionAsync(executionClient, @case.CaseId, @case.CaseVersion, "idem-key-recon-success-1", request);
        var execution = await execResponse.Content.ReadFromJsonAsync<ExecutionResponse>(JsonOptions);
        var caseAfterExec = await (await approverClient.GetAsync($"/v1/cases/{@case.CaseId}")).Content.ReadFromJsonAsync<CaseResponse>(JsonOptions);

        var resolveRequest = new HttpRequestMessage(HttpMethod.Post, $"/v1/cases/{@case.CaseId}/reconciliations/{execution!.ExecutionId}/resolve")
        {
            Content = JsonContent.Create(new ResolveReconciliationRequest(caseAfterExec!.CaseVersion, ReconciliationResolution.ConfirmedSucceeded, "confirmed via bank statement"), options: JsonOptions),
        };
        var resolveResponse = await approverClient.SendAsync(resolveRequest);
        var resolvedExecution = await resolveResponse.Content.ReadFromJsonAsync<ExecutionResponse>(JsonOptions);

        Assert.Equal(HttpStatusCode.OK, resolveResponse.StatusCode);
        Assert.Equal(ExecutionStatus.Reconciled, resolvedExecution!.Status);

        var caseAfterResolve = await (await approverClient.GetAsync($"/v1/cases/{@case.CaseId}")).Content.ReadFromJsonAsync<CaseResponse>(JsonOptions);
        Assert.Equal(CaseState.Executed, caseAfterResolve!.State);
    }

    [Fact]
    public async Task ResolveReconciliation_ConfirmedFailed_TransitionsCaseToFailed()
    {
        var recommenderClient = CreateClient("tenant-recon-failed", "recommender-exec-6");
        var approverClient = CreateClient("tenant-recon-failed", "approver-exec-6");
        var executionClient = CreateExecutionClient("tenant-recon-failed", "execution-service-6");
        var (@case, approvalId, recommendationVersion, evidenceIds) = await BringToApprovedAsync(recommenderClient, approverClient, "tenant-recon-failed", "ext-recon-failed-1");

        var request = new RequestExecutionRequest(
            @case.CaseVersion, approvalId, recommendationVersion, CommandType.MockRefund, "ambiguous-command-hash-6", evidenceIds);
        var execResponse = await RequestExecutionAsync(executionClient, @case.CaseId, @case.CaseVersion, "idem-key-recon-failed-1", request);
        var execution = await execResponse.Content.ReadFromJsonAsync<ExecutionResponse>(JsonOptions);
        var caseAfterExec = await (await approverClient.GetAsync($"/v1/cases/{@case.CaseId}")).Content.ReadFromJsonAsync<CaseResponse>(JsonOptions);

        var resolveRequest = new HttpRequestMessage(HttpMethod.Post, $"/v1/cases/{@case.CaseId}/reconciliations/{execution!.ExecutionId}/resolve")
        {
            Content = JsonContent.Create(new ResolveReconciliationRequest(caseAfterExec!.CaseVersion, ReconciliationResolution.ConfirmedFailed, "confirmed failure via ledger"), options: JsonOptions),
        };
        var resolveResponse = await approverClient.SendAsync(resolveRequest);
        var resolvedExecution = await resolveResponse.Content.ReadFromJsonAsync<ExecutionResponse>(JsonOptions);

        Assert.Equal(ExecutionStatus.Failed, resolvedExecution!.Status);

        var caseAfterResolve = await (await approverClient.GetAsync($"/v1/cases/{@case.CaseId}")).Content.ReadFromJsonAsync<CaseResponse>(JsonOptions);
        Assert.Equal(CaseState.Failed, caseAfterResolve!.State);
    }
}
