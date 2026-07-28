using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Backoffice.Application.Cases;
using Backoffice.Application.Identity;
using Backoffice.Application.Policy;
using Backoffice.Domain.Cases;
using Microsoft.Extensions.DependencyInjection;

namespace Backoffice.Api.Tests;

/// <summary>
/// Task 7.6's dedicated policy-mechanism tests: fail-closed behavior when the PDP is
/// unreachable, purpose-binding mismatch denial, and obligation enforcement — as distinct
/// from the per-endpoint tests elsewhere that exercise real business scenarios through OPA.
/// </summary>
public class PolicyEnforcementTests(BackofficeApiFactory factory) : IClassFixture<BackofficeApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(ScreamingSnakeCaseNamingPolicy.Instance) },
    };

    [Fact]
    public async Task CreateCase_WhenPolicyDecisionPointUnreachable_ReturnsServiceUnavailable()
    {
        // Reuses the same running OPA subprocess/app pipeline but points Opa:BaseUrl at a
        // closed port, so the typed HttpClient fails to connect — this must fail closed
        // (503), never fail open.
        using var unreachablePdpFactory = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Opa:BaseUrl", "http://127.0.0.1:1"));

        var client = unreachablePdpFactory.CreateClient();
        client.DefaultRequestHeaders.Add(RequestContext.TenantHeader, "tenant-pdp-unavailable");
        client.DefaultRequestHeaders.Add(RequestContext.SubjectHeader, "test-actor");
        client.DefaultRequestHeaders.Add(RequestContext.RolesHeader, "case-manager");

        var request = new CreateCaseRequest("ext-pdp-unavailable-1", DisputeType.CardPurchase, Channel.App, Priority.Normal, new MoneyDto("BRL", "150.00"));
        var response = await client.PostAsJsonAsync("/v1/cases", request, JsonOptions);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Evaluate_PurposeDoesNotMatchAction_IsDenied()
    {
        // Handlers always construct the correct purpose for their action, so this can only
        // be exercised by calling the PDP client directly with a deliberately mismatched
        // purpose — proving policies/authorization.rego's purpose_matches_action gate works
        // independent of any particular handler wiring it correctly.
        using var scope = factory.Services.CreateScope();
        var pdpClient = scope.ServiceProvider.GetRequiredService<IPolicyDecisionClient>();

        var input = new AuthorizationInput(
            new PolicySubject("test-actor", PolicySubjectTypes.Human, "tenant-purpose-mismatch", ["case-manager"]),
            PolicyActions.CaseCreate,
            new PolicyResource(PolicyResourceTypes.Case, Guid.NewGuid().ToString(), "tenant-purpose-mismatch"),
            PolicyPurposes.Audit, // case.create requires CASE_MANAGEMENT/CASE_PROCESSING, not AUDIT.
            Guid.NewGuid().ToString(),
            new Dictionary<string, object?>());

        var decision = await pdpClient.EvaluateAsync(input);

        Assert.True(decision.PdpAvailable);
        Assert.False(decision.Allow);
    }

    [Fact]
    public async Task Enforce_ObligationNotConfirmedServerSide_ThrowsObligationNotSatisfied()
    {
        // Pure unit test of PolicyEnforcer against a fake IPolicyDecisionClient — no HTTP or
        // OPA involved. Simulates OPA allowing an action but returning an obligation
        // (verify-case-version) that the caller cannot confirm as satisfied.
        var fakeClient = new AlwaysAllowWithObligationClient("verify-case-version");
        var enforcer = new PolicyEnforcer(fakeClient, new NullCallerIdentityAccessor());

        var input = new AuthorizationInput(
            new PolicySubject("test-actor", PolicySubjectTypes.Human, "tenant-obligation", ["case-manager"]),
            PolicyActions.CaseCancel,
            new PolicyResource(PolicyResourceTypes.Case, Guid.NewGuid().ToString(), "tenant-obligation", "CREATED"),
            PolicyPurposes.CaseProcessing,
            Guid.NewGuid().ToString(),
            new Dictionary<string, object?>());

        await Assert.ThrowsAsync<ObligationNotSatisfiedException>(() =>
            enforcer.EnforceAsync(input, obligationResults: new Dictionary<string, bool> { ["verify-case-version"] = false }));

        await Assert.ThrowsAsync<ObligationNotSatisfiedException>(() =>
            enforcer.EnforceAsync(input, obligationResults: null));
    }

    [Fact]
    public async Task Enforce_ObligationConfirmedServerSide_Succeeds()
    {
        var fakeClient = new AlwaysAllowWithObligationClient("verify-case-version");
        var enforcer = new PolicyEnforcer(fakeClient, new NullCallerIdentityAccessor());

        var input = new AuthorizationInput(
            new PolicySubject("test-actor", PolicySubjectTypes.Human, "tenant-obligation-ok", ["case-manager"]),
            PolicyActions.CaseCancel,
            new PolicyResource(PolicyResourceTypes.Case, Guid.NewGuid().ToString(), "tenant-obligation-ok", "CREATED"),
            PolicyPurposes.CaseProcessing,
            Guid.NewGuid().ToString(),
            new Dictionary<string, object?>());

        var decision = await enforcer.EnforceAsync(input, obligationResults: new Dictionary<string, bool> { ["verify-case-version"] = true });

        Assert.True(decision.Allow);
    }

    private sealed class AlwaysAllowWithObligationClient(params string[] obligations) : IPolicyDecisionClient
    {
        public Task<AuthorizationDecision> EvaluateAsync(AuthorizationInput input, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AuthorizationDecision(true, "allowed", obligations));
    }
}
