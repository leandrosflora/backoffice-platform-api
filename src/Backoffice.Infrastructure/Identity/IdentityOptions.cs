namespace Backoffice.Infrastructure.Identity;

/// <summary>Bound from configuration section "Identity" — matches
/// docs/security/workload-identity.md's claim table and the Python reference's `Settings`.</summary>
public sealed class IdentityOptions
{
    /// <summary>"headers" (default/dev, matches sections 1–10) or "jwt" (secure profile).</summary>
    public string Mode { get; set; } = "headers";
    /// <summary>Local Ed25519 public key. When empty, Authority/MetadataAddress discovery is used.</summary>
    public string PublicKeyPath { get; set; } = "";
    /// <summary>OIDC issuer base URL used to derive .well-known/openid-configuration.</summary>
    public string Authority { get; set; } = "";
    /// <summary>Optional explicit discovery URL. Takes precedence over Authority.</summary>
    public string MetadataAddress { get; set; } = "";
    public bool RequireHttpsMetadata { get; set; } = true;
    public string Issuer { get; set; } = "https://identity.local";
    public string Audience { get; set; } = "intelligent-backoffice-api";
    public string[] AllowedAlgorithms { get; set; } = ["RS256", "PS256", "ES256", "EdDSA"];
    public int MaxTtlSeconds { get; set; } = 300;
}
