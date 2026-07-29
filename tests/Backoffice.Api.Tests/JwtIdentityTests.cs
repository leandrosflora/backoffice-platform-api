using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Backoffice.Application.Cases;
using Backoffice.Application.Identity;
using Backoffice.Domain.Cases;
using Backoffice.Infrastructure.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using JwtSecurityAlgorithms = Microsoft.IdentityModel.Tokens.SecurityAlgorithms;

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

    private static OpenIdConnectConfiguration OidcConfiguration(string issuer, SecurityKey signingKey)
    {
        var configuration = new OpenIdConnectConfiguration { Issuer = issuer };
        configuration.SigningKeys.Add(signingKey);
        return configuration;
    }

    private static string CreateRsaToken(
        RsaSecurityKey signingKey,
        string issuer,
        string audience,
        string subject = "oidc-user",
        string purpose = "CASE_MANAGEMENT")
    {
        var now = DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = now,
            Expires = now.AddMinutes(2),
            SigningCredentials = new SigningCredentials(signingKey, JwtSecurityAlgorithms.RsaSha256),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = subject,
                ["subject_type"] = "HUMAN",
                ["tenant_id"] = "tenant-oidc",
                ["roles"] = new[] { "case-manager" },
                ["purpose"] = purpose,
                ["jti"] = Guid.NewGuid().ToString(),
            },
        });
    }

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

    [Fact]
    public async Task AuthorityLimit_IsDerivedFromSignedClaimAndSpoofedHeaderIsIgnored()
    {
        var keys = GenerateKeyPair();
        var token = DevIdentityGenerator.CreateToken(
            keys.PrivateKeyPath, Issuer, Audience, subject: "approver-claim", subjectType: "HUMAN",
            tenantId: "tenant-authority", roles: ["approver"], purpose: "APPROVAL",
            authorityLimit: 750.50m);
        var validator = new JwtIdentityValidator(Options.Create(new IdentityOptions
        {
            PublicKeyPath = keys.PublicKeyPath,
            Issuer = Issuer,
            Audience = Audience,
            MaxTtlSeconds = 300,
        }));

        var identity = await validator.ValidateAsync(token);
        var context = new DefaultHttpContext();
        context.Items["ResolvedIdentity"] = identity;
        context.Request.Headers[RequestContext.AuthorityLimitHeader] = "999999.00";

        Assert.Equal(750.50m, identity.AuthorityLimit);
        Assert.Equal(750.50m, RequestContext.GetAuthorityLimit(context.Request));
    }

    [Fact]
    public async Task MissingAuthorityClaim_FailsClosedAndNegativeClaimIsRejected()
    {
        var keys = GenerateKeyPair();
        var validator = new JwtIdentityValidator(Options.Create(new IdentityOptions
        {
            PublicKeyPath = keys.PublicKeyPath,
            Issuer = Issuer,
            Audience = Audience,
            MaxTtlSeconds = 300,
        }));
        var tokenWithoutLimit = DevIdentityGenerator.CreateToken(
            keys.PrivateKeyPath, Issuer, Audience, subject: "approver-no-limit", subjectType: "HUMAN",
            tenantId: "tenant-authority", roles: ["approver"], purpose: "APPROVAL");
        var identity = await validator.ValidateAsync(tokenWithoutLimit);
        var context = new DefaultHttpContext();
        context.Items["ResolvedIdentity"] = identity;

        Assert.Null(identity.AuthorityLimit);
        Assert.Equal(0m, RequestContext.GetAuthorityLimit(context.Request));

        var tokenWithNegativeLimit = DevIdentityGenerator.CreateToken(
            keys.PrivateKeyPath, Issuer, Audience, subject: "approver-negative", subjectType: "HUMAN",
            tenantId: "tenant-authority", roles: ["approver"], purpose: "APPROVAL",
            authorityLimit: -1m);
        var exception = await Assert.ThrowsAsync<JwtValidationException>(
            () => validator.ValidateAsync(tokenWithNegativeLimit));
        Assert.Equal("invalid-authority-limit", exception.Reason);
    }

    [Fact]
    public async Task OidcConfiguration_ValidatesStandardRsaAccessToken()
    {
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "oidc-key-1" };
        const string oidcIssuer = "https://issuer.example.test";
        var configurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(
            OidcConfiguration(oidcIssuer, key));
        var validator = new JwtIdentityValidator(
            Options.Create(new IdentityOptions
            {
                Audience = Audience,
                AllowedAlgorithms = [JwtSecurityAlgorithms.RsaSha256],
                MaxTtlSeconds = 300,
            }),
            configurationManager);

        var identity = await validator.ValidateAsync(CreateRsaToken(key, oidcIssuer, Audience));

        Assert.Equal("oidc-user", identity.ActorId);
        Assert.Equal("tenant-oidc", identity.TenantId);
        Assert.Equal("CASE_MANAGEMENT", identity.Purpose);
        Assert.Equal("SIGNED_JWT", identity.AuthenticationMethod);
    }

    [Fact]
    public async Task OidcUnknownKid_RefreshesConfigurationAndAcceptsRotatedKey()
    {
        using var oldRsa = RSA.Create(2048);
        using var newRsa = RSA.Create(2048);
        var oldKey = new RsaSecurityKey(oldRsa) { KeyId = "old-key" };
        var newKey = new RsaSecurityKey(newRsa) { KeyId = "rotated-key" };
        const string oidcIssuer = "https://issuer.example.test";
        var configurationManager = new RotatingConfigurationManager(
            OidcConfiguration(oidcIssuer, oldKey),
            OidcConfiguration(oidcIssuer, newKey));
        var validator = new JwtIdentityValidator(
            Options.Create(new IdentityOptions
            {
                Audience = Audience,
                AllowedAlgorithms = [JwtSecurityAlgorithms.RsaSha256],
                MaxTtlSeconds = 300,
            }),
            configurationManager);

        var identity = await validator.ValidateAsync(
            CreateRsaToken(newKey, oidcIssuer, Audience, subject: "rotated-user"));

        Assert.Equal("rotated-user", identity.ActorId);
        Assert.Equal(1, configurationManager.RefreshCount);
        Assert.Equal(2, configurationManager.GetConfigurationCount);
    }

    private sealed class RotatingConfigurationManager(
        OpenIdConnectConfiguration initial,
        OpenIdConnectConfiguration rotated) : IConfigurationManager<OpenIdConnectConfiguration>
    {
        private bool _refreshRequested;
        public int RefreshCount { get; private set; }
        public int GetConfigurationCount { get; private set; }

        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel)
        {
            GetConfigurationCount++;
            return Task.FromResult(_refreshRequested ? rotated : initial);
        }

        public void RequestRefresh()
        {
            RefreshCount++;
            _refreshRequested = true;
        }
    }
}
