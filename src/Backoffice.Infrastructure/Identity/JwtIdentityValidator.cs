using Backoffice.Application.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Backoffice.Infrastructure.Identity;

/// <summary>
/// Validates a bearer JWT against the claim set and TTL rules in
/// docs/security/workload-identity.md (spec: identity-security, "Short-TTL EdDSA JWT
/// validation" / "Subject type and purpose validation"). Signature verification runs
/// through the standard `JsonWebTokenHandler` pipeline via <see cref="EdDsaSecurityKey"/>;
/// everything else (required-claims presence, TTL ≤ configured max, subject_type/purpose
/// whitelist) is checked explicitly here, matching the Python reference's `_jwt_context`
/// exactly rather than relying on the library's more generic validation.
/// </summary>
public sealed class JwtIdentityValidator : IJwtIdentityValidator
{
    private static readonly HashSet<string> ValidSubjectTypes = ["HUMAN", "WORKLOAD"];
    private static readonly HashSet<string> ValidPurposes = ["CASE_MANAGEMENT", "OPERATIONS", "AUDIT", "EXECUTION", "APPROVAL"];
    private static readonly string[] RequiredClaims =
        ["iss", "aud", "sub", "subject_type", "tenant_id", "roles", "purpose", "iat", "exp", "jti"];

    private readonly IdentityOptions _options;
    private readonly EdDsaSecurityKey _signingKey;
    private readonly JsonWebTokenHandler _handler = new();

    public JwtIdentityValidator(IOptions<IdentityOptions> options)
    {
        _options = options.Value;
        _signingKey = new EdDsaSecurityKey(EdDsaKeyLoader.LoadPublicKey(_options.PublicKeyPath));
    }

    public ResolvedIdentity Validate(string bearerToken)
    {
        var validationParameters = new TokenValidationParameters
        {
            ValidIssuer = _options.Issuer,
            ValidAudience = _options.Audience,
            IssuerSigningKey = _signingKey,
            ValidAlgorithms = [SecurityAlgorithms.EdDsa],
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            // TokenValidationParameters has its own CryptoProviderFactory (defaulting to the
            // global CryptoProviderFactory.Default, which knows nothing about "EdDSA") that
            // otherwise takes precedence over the signing key's own factory during
            // signature verification — without this, verification silently uses the wrong
            // factory and every token, valid or not, fails signature validation.
            CryptoProviderFactory = _signingKey.CryptoProviderFactory,
        };

        TokenValidationResult result;
        try
        {
            result = _handler.ValidateTokenAsync(bearerToken, validationParameters).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            throw new JwtValidationException("invalid-workload-token", exception);
        }

        if (!result.IsValid || result.SecurityToken is not JsonWebToken token)
        {
            throw new JwtValidationException("invalid-workload-token", result.Exception);
        }

        foreach (var claim in RequiredClaims)
        {
            if (!token.TryGetPayloadValue<object>(claim, out _))
            {
                throw new JwtValidationException("missing-required-claim");
            }
        }

        if (!token.TryGetPayloadValue<long>("iat", out var issuedAt) || !token.TryGetPayloadValue<long>("exp", out var expiresAt))
        {
            throw new JwtValidationException("invalid-token-ttl");
        }

        var ttl = expiresAt - issuedAt;
        if (ttl <= 0 || ttl > _options.MaxTtlSeconds)
        {
            throw new JwtValidationException("invalid-token-ttl");
        }

        var subjectType = token.GetPayloadValue<string>("subject_type").ToUpperInvariant();
        if (!ValidSubjectTypes.Contains(subjectType))
        {
            throw new JwtValidationException("invalid-subject-type");
        }

        var purpose = token.GetPayloadValue<string>("purpose").ToUpperInvariant();
        if (!ValidPurposes.Contains(purpose))
        {
            throw new JwtValidationException("invalid-purpose");
        }

        if (!token.TryGetPayloadValue<string[]>("roles", out var roles) || roles.Length == 0 || roles.Any(string.IsNullOrWhiteSpace))
        {
            throw new JwtValidationException("invalid-roles");
        }

        var tenantId = token.GetPayloadValue<string>("tenant_id");
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new JwtValidationException("invalid-token-context");
        }

        return new ResolvedIdentity(
            ActorId: token.GetPayloadValue<string>("sub"),
            SubjectType: subjectType,
            Roles: roles,
            TenantId: tenantId,
            AuthenticationMethod: "SIGNED_JWT",
            TokenId: token.GetPayloadValue<string>("jti"));
    }
}
