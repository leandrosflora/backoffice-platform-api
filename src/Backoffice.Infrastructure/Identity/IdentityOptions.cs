namespace Backoffice.Infrastructure.Identity;

/// <summary>Bound from configuration section "Identity" — matches
/// docs/security/workload-identity.md's claim table and the Python reference's `Settings`.</summary>
public sealed class IdentityOptions
{
    /// <summary>"headers" (default/dev, matches sections 1–10) or "jwt" (secure profile).</summary>
    public string Mode { get; set; } = "headers";
    public string PublicKeyPath { get; set; } = "";
    public string Issuer { get; set; } = "https://identity.local";
    public string Audience { get; set; } = "intelligent-backoffice-api";
    public int MaxTtlSeconds { get; set; } = 300;
}
