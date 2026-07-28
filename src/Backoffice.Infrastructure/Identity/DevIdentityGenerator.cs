using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NSec.Cryptography;

namespace Backoffice.Infrastructure.Identity;

/// <summary>
/// .NET equivalent of `scripts/generate_dev_identity.py` (task 11.5): generates an ephemeral
/// Ed25519 key pair as PEM files interchangeable with the Python script's output, and mints
/// short-TTL EdDSA JWTs against them for local dev/test use — never for production (see
/// docs/security/workload-identity.md's own "Produção" section: real deployments need
/// OIDC/SPIFFE, not this).
/// </summary>
public static class DevIdentityGenerator
{
    /// <summary>Generates a key pair and writes `identity-private.pem`/`identity-public.pem`
    /// (PKCS8 / SubjectPublicKeyInfo, matching the Python script byte-for-byte in format)
    /// under <paramref name="outputDirectory"/>.</summary>
    public static void WriteKeyPair(string outputDirectory, bool force = false)
    {
        Directory.CreateDirectory(outputDirectory);
        var privatePath = Path.Combine(outputDirectory, "identity-private.pem");
        var publicPath = Path.Combine(outputDirectory, "identity-public.pem");

        if (!force && (File.Exists(privatePath) || File.Exists(publicPath)))
        {
            throw new InvalidOperationException("identity files already exist; pass force to replace the local-only keys");
        }

        using var key = Key.Create(SignatureAlgorithm.Ed25519, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        File.WriteAllBytes(privatePath, key.Export(KeyBlobFormat.PkixPrivateKeyText));
        File.WriteAllBytes(publicPath, key.PublicKey.Export(KeyBlobFormat.PkixPublicKeyText));
    }

    /// <summary>Mints a signed JWT with the required claim set (spec: identity-security),
    /// TTL = <paramref name="ttlSeconds"/> from now.</summary>
    public static string CreateToken(
        string privateKeyPemPath,
        string issuer,
        string audience,
        string subject,
        string subjectType,
        string tenantId,
        IReadOnlyList<string> roles,
        string purpose,
        int ttlSeconds = 60,
        string? jti = null)
    {
        var privateKey = EdDsaKeyLoader.LoadPrivateKey(privateKeyPemPath);
        var signingKey = new EdDsaSecurityKey(privateKey);
        var now = DateTime.UtcNow;

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = now,
            Expires = now.AddSeconds(ttlSeconds),
            SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.EdDsa),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = subject,
                ["subject_type"] = subjectType,
                ["tenant_id"] = tenantId,
                ["roles"] = roles,
                ["purpose"] = purpose,
                ["jti"] = jti ?? Guid.NewGuid().ToString(),
            },
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
