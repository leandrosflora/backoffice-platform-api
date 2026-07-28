using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Backoffice.Application.Cases;
using Backoffice.Domain.Cases;
using Backoffice.Infrastructure.Identity;
using Microsoft.Extensions.Configuration;

namespace Backoffice.Api.Tests;

/// <summary>
/// Exercises the secure/jwt identity profile end-to-end against a real Ed25519 key pair and
/// the real NSec-backed signature provider (spec: identity-security) — no mocked crypto.
/// Each test builds its own <see cref="BackofficeApiFactory"/> derivative via
/// <c>WithWebHostBuilder</c> pointed at a freshly generated dev key pair, since jwt mode is
/// off by default (matching every other section's header-based tests).
/// </summary>
public class JwtIdentityTests(BackofficeApiFactory factory) : IClassFixture<BackofficeApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(ScreamingSnakeCaseNamingPolicy.Instance) },
    };

    private const string Issuer = "https://identity.local";
    private const string Audience = "intelligent-backoffice-api";

    private sealed record DevKeyPair(string PrivateKeyPath, string PublicKeyPath);

    private static DevKeyPair GenerateKeyPair()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"backoffice-jwt-test-{Guid.NewGuid():N}");
        DevIdentityGenerator.WriteKeyPair(dir);
        return new DevKeyPair(Path.Combine(dir, "identity-private.pem"), Path.Combine(dir, "identity-public.pem"));
    }

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> CreateJwtModeFactory(string publicKeyPath) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Identity:Mode"] = "jwt",
                ["Identity:PublicKeyPath"] = publicKeyPath,
                ["Identity:Issuer"] = Issuer,
                ["Identity:Audience"] = Audience,
                ["Identity:MaxTtlSeconds"] = "300",
            });
        }));

    private static HttpClient CreateAuthorizedClient(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> jwtFactory, string token)
    {
        var client = jwtFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static Task<HttpResponseMessage> CreateCaseAsync(HttpClient client) =>
        client.PostAsJsonAsync(
            "/v1/cases",
            new CreateCaseRequest("ext-jwt-1", DisputeType.CardPurchase, Channel.App, Priority.Normal, new MoneyDto("BRL", "150.00")),
            JsonOptions);

    [Fact]
    public async Task ValidToken_IsAcceptedAndDerivesIdentityFromClaims()
    {
        var keys = GenerateKeyPair();
        using var jwtFactory = CreateJwtModeFactory(keys.PublicKeyPath);

        var token = DevIdentityGenerator.CreateToken(
            keys.PrivateKeyPath, Issuer, Audience, subject: "approver-1", subjectType: "HUMAN",
            tenantId: "tenant-jwt-ok", roles: ["case-manager"], purpose: "CASE_MANAGEMENT");

        var client = CreateAuthorizedClient(jwtFactory, token);
        var response = await CreateCaseAsync(client);
        var rawBody = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"Expected Created, got {response.StatusCode}: {rawBody}");

        var body = JsonSerializer.Deserialize<CaseResponse>(rawBody, JsonOptions);
        Assert.Equal("tenant-jwt-ok", body!.TenantId);
    }

    [Fact]
    public async Task MissingBearerToken_ReturnsUnauthorized()
    {
        var keys = GenerateKeyPair();
        using var jwtFactory = CreateJwtModeFactory(keys.PublicKeyPath);

        var client = jwtFactory.CreateClient();
        var response = await CreateCaseAsync(client);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ExpiredToken_ReturnsUnauthorized()
    {
        var keys = GenerateKeyPair();
        using var jwtFactory = CreateJwtModeFactory(keys.PublicKeyPath);

        // TTL of 1 second, then waiting past it, so `exp` has genuinely passed.
        var token = DevIdentityGenerator.CreateToken(
            keys.PrivateKeyPath, Issuer, Audience, subject: "actor-1", subjectType: "HUMAN",
            tenantId: "tenant-jwt-expired", roles: ["case-manager"], purpose: "CASE_MANAGEMENT", ttlSeconds: 1);
        await Task.Delay(TimeSpan.FromSeconds(2));

        var client = CreateAuthorizedClient(jwtFactory, token);
        var response = await CreateCaseAsync(client);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task OverLongTtlToken_ReturnsUnauthorized()
    {
        var keys = GenerateKeyPair();
        using var jwtFactory = CreateJwtModeFactory(keys.PublicKeyPath);

        // exp - iat = 600s > the 300s max, even though the token is not yet expired by clock time.
        var token = DevIdentityGenerator.CreateToken(
            keys.PrivateKeyPath, Issuer, Audience, subject: "actor-1", subjectType: "HUMAN",
            tenantId: "tenant-jwt-overttl", roles: ["case-manager"], purpose: "CASE_MANAGEMENT", ttlSeconds: 600);

        var client = CreateAuthorizedClient(jwtFactory, token);
        var response = await CreateCaseAsync(client);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WrongSigningKey_ReturnsUnauthorized()
    {
        var keys = GenerateKeyPair();
        var otherKeys = GenerateKeyPair(); // a different key pair the server never trusts
        using var jwtFactory = CreateJwtModeFactory(keys.PublicKeyPath);

        var token = DevIdentityGenerator.CreateToken(
            otherKeys.PrivateKeyPath, Issuer, Audience, subject: "actor-1", subjectType: "HUMAN",
            tenantId: "tenant-jwt-wrongkey", roles: ["case-manager"], purpose: "CASE_MANAGEMENT");

        var client = CreateAuthorizedClient(jwtFactory, token);
        var response = await CreateCaseAsync(client);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task InvalidSubjectType_ReturnsUnauthorized()
    {
        var keys = GenerateKeyPair();
        using var jwtFactory = CreateJwtModeFactory(keys.PublicKeyPath);

        var token = DevIdentityGenerator.CreateToken(
            keys.PrivateKeyPath, Issuer, Audience, subject: "actor-1", subjectType: "ROBOT",
            tenantId: "tenant-jwt-badsubjecttype", roles: ["case-manager"], purpose: "CASE_MANAGEMENT");

        var client = CreateAuthorizedClient(jwtFactory, token);
        var response = await CreateCaseAsync(client);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task InvalidPurpose_ReturnsUnauthorized()
    {
        var keys = GenerateKeyPair();
        using var jwtFactory = CreateJwtModeFactory(keys.PublicKeyPath);

        var token = DevIdentityGenerator.CreateToken(
            keys.PrivateKeyPath, Issuer, Audience, subject: "actor-1", subjectType: "HUMAN",
            tenantId: "tenant-jwt-badpurpose", roles: ["case-manager"], purpose: "NOT_A_REAL_PURPOSE");

        var client = CreateAuthorizedClient(jwtFactory, token);
        var response = await CreateCaseAsync(client);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SpoofedIdentityHeaders_AreIgnoredInSecureProfile()
    {
        var keys = GenerateKeyPair();
        using var jwtFactory = CreateJwtModeFactory(keys.PublicKeyPath);

        // A validly signed token for a *different* tenant/role than the spoofing headers claim.
        var token = DevIdentityGenerator.CreateToken(
            keys.PrivateKeyPath, Issuer, Audience, subject: "real-actor", subjectType: "HUMAN",
            tenantId: "tenant-jwt-real", roles: ["case-manager"], purpose: "CASE_MANAGEMENT");

        var client = CreateAuthorizedClient(jwtFactory, token);
        client.DefaultRequestHeaders.Add(RequestContext.TenantHeader, "tenant-jwt-spoofed");
        client.DefaultRequestHeaders.Add(RequestContext.SubjectHeader, "spoofed-actor");
        client.DefaultRequestHeaders.Add(RequestContext.RolesHeader, "platform-operator,auditor");

        var response = await CreateCaseAsync(client);
        var body = await response.Content.ReadFromJsonAsync<CaseResponse>(JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        // The case was created under the JWT's tenant, not the spoofed header's tenant —
        // proving the headers had zero effect on the resolved identity.
        Assert.Equal("tenant-jwt-real", body!.TenantId);
    }
}
