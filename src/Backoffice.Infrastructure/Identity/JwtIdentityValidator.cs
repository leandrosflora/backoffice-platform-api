using System.Globalization;
using System.Text.Json;
using Backoffice.Application.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Backoffice.Infrastructure.Identity;

/// <summary>
/// Validates short-lived bearer JWTs either with the local Ed25519 development key or with
/// OIDC discovery/JWKS. Discovery mode follows issuer metadata, caches signing keys through
/// ConfigurationManager and requests an immediate metadata refresh after an unknown-kid
/// failure, which supports normal provider key rotation without accepting caller keys.
/// Domain claims and the maximum token TTL are validated explicitly after cryptographic
/// validation.
/// </summary>
public sealed class JwtIdentityValidator : IJwtIdentityValidator
{
    private static readonly HashSet<string> ValidSubjectTypes = ["HUMAN", "WORKLOAD"];
    private static readonly HashSet<string> ValidPurposes = ["CASE_MANAGEMENT", "OPERATIONS", "AUDIT", "EXECUTION", "APPROVAL"];
    private static readonly string[] RequiredClaims =
        ["iss", "aud", "sub", "subject_type", "tenant_id", "roles", "purpose", "iat", "exp", "jti"];

    private readonly IdentityOptions _options;
    private readonly EdDsaSecurityKey? _localSigningKey;
    private readonly IConfigurationManager<OpenIdConnectConfiguration>? _configurationManager;
    private readonly JsonWebTokenHandler _handler = new();

    public JwtIdentityValidator(IOptions<IdentityOptions> options)
        : this(options, configurationManager: null)
    {
    }

    public JwtIdentityValidator(
        IOptions<IdentityOptions> options,
        IConfigurationManager<OpenIdConnectConfiguration>? configurationManager)
    {
        _options = options.Value;

        if (!string.IsNullOrWhiteSpace(_options.PublicKeyPath))
        {
            _localSigningKey = new EdDsaSecurityKey(EdDsaKeyLoader.LoadPublicKey(_options.PublicKeyPath));
            return;
        }

        if (configurationManager is not null)
        {
            _configurationManager = configurationManager;
            return;
        }

        var metadataAddress = ResolveMetadataAddress(_options);
        var documentRetriever = new HttpDocumentRetriever { RequireHttps = _options.RequireHttpsMetadata };
        _configurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            metadataAddress,
            new OpenIdConnectConfigurationRetriever(),
            documentRetriever);
    }

    public async Task<ResolvedIdentity> ValidateAsync(
        string bearerToken,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (_localSigningKey is not null)
            {
                var localResult = await _handler.ValidateTokenAsync(
                    bearerToken,
                    CreateLocalValidationParameters());
                return ResolveIdentity(localResult);
            }

            var configuration = await _configurationManager!.GetConfigurationAsync(cancellationToken);
            var result = await _handler.ValidateTokenAsync(
                bearerToken,
                CreateOidcValidationParameters(configuration));

            if (!result.IsValid && IsUnknownSigningKey(result.Exception))
            {
                _configurationManager.RequestRefresh();
                configuration = await _configurationManager.GetConfigurationAsync(cancellationToken);
                result = await _handler.ValidateTokenAsync(
                    bearerToken,
                    CreateOidcValidationParameters(configuration));
            }

            return ResolveIdentity(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (JwtValidationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new JwtValidationException("invalid-workload-token", exception);
        }
    }

    private TokenValidationParameters CreateLocalValidationParameters() =>
        new()
        {
            ValidIssuer = _options.Issuer,
            ValidAudience = _options.Audience,
            IssuerSigningKey = _localSigningKey,
            ValidAlgorithms = [SecurityAlgorithms.EdDsa],
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            CryptoProviderFactory = _localSigningKey!.CryptoProviderFactory,
        };

    private TokenValidationParameters CreateOidcValidationParameters(OpenIdConnectConfiguration configuration) =>
        new()
        {
            ValidIssuer = configuration.Issuer,
            ValidAudience = _options.Audience,
            IssuerSigningKeys = configuration.SigningKeys,
            ValidAlgorithms = _options.AllowedAlgorithms,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
        };

    private ResolvedIdentity ResolveIdentity(TokenValidationResult result)
    {
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

        if (!token.TryGetPayloadValue<long>("iat", out var issuedAt)
            || !token.TryGetPayloadValue<long>("exp", out var expiresAt))
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

        var roles = ReadRoles(token);
        if (roles.Length == 0 || roles.Any(string.IsNullOrWhiteSpace))
        {
            throw new JwtValidationException("invalid-roles");
        }

        var tenantId = token.GetPayloadValue<string>("tenant_id");
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new JwtValidationException("invalid-token-context");
        }

        var authorityLimit = ReadAuthorityLimit(token);
        if (authorityLimit < 0)
        {
            throw new JwtValidationException("invalid-authority-limit");
        }

        return new ResolvedIdentity(
            ActorId: token.GetPayloadValue<string>("sub"),
            SubjectType: subjectType,
            Roles: roles,
            TenantId: tenantId,
            AuthenticationMethod: "SIGNED_JWT",
            TokenId: token.GetPayloadValue<string>("jti"),
            Purpose: purpose,
            AuthorityLimit: authorityLimit);
    }

    private static string[] ReadRoles(JsonWebToken token)
    {
        if (token.TryGetPayloadValue<string[]>("roles", out var arrayRoles))
        {
            return arrayRoles;
        }

        return token.TryGetPayloadValue<string>("roles", out var stringRoles)
            ? stringRoles.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];
    }

    private static decimal? ReadAuthorityLimit(JsonWebToken token)
    {
        if (!token.TryGetPayloadValue<object>("authority_limit", out var rawValue))
        {
            return null;
        }

        var parsed = rawValue switch
        {
            decimal value => value,
            long value => value,
            int value => value,
            double value when double.IsFinite(value) => Convert.ToDecimal(value),
            string value when decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) => number,
            JsonElement { ValueKind: JsonValueKind.Number } element when element.TryGetDecimal(out var number) => number,
            JsonElement { ValueKind: JsonValueKind.String } element
                when decimal.TryParse(element.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var number) => number,
            _ => throw new JwtValidationException("invalid-authority-limit"),
        };

        return parsed;
    }

    private static bool IsUnknownSigningKey(Exception? exception) =>
        exception is SecurityTokenSignatureKeyNotFoundException
        || exception?.InnerException is SecurityTokenSignatureKeyNotFoundException;

    private static string ResolveMetadataAddress(IdentityOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.MetadataAddress))
        {
            return options.MetadataAddress;
        }

        if (!string.IsNullOrWhiteSpace(options.Authority))
        {
            return $"{options.Authority.TrimEnd('/')}/.well-known/openid-configuration";
        }

        throw new InvalidOperationException(
            "JWT mode requires Identity:PublicKeyPath or Identity:Authority/MetadataAddress.");
    }
}
