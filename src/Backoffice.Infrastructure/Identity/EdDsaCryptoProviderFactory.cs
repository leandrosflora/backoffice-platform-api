using Microsoft.IdentityModel.Tokens;

namespace Backoffice.Infrastructure.Identity;

/// <summary>Routes "EdDSA" signing/verification requests to <see cref="NSecEdDsaSignatureProvider"/>;
/// everything else falls back to the base factory unchanged.</summary>
public sealed class EdDsaCryptoProviderFactory : CryptoProviderFactory
{
    public override bool IsSupportedAlgorithm(string algorithm, SecurityKey key) =>
        algorithm == SecurityAlgorithms.EdDsa && key is EdDsaSecurityKey
        || base.IsSupportedAlgorithm(algorithm, key);

    public override SignatureProvider CreateForVerifying(SecurityKey key, string algorithm) =>
        algorithm == SecurityAlgorithms.EdDsa && key is EdDsaSecurityKey edDsaKey
            ? new NSecEdDsaSignatureProvider(edDsaKey, algorithm)
            : base.CreateForVerifying(key, algorithm);

    public override SignatureProvider CreateForSigning(SecurityKey key, string algorithm) =>
        algorithm == SecurityAlgorithms.EdDsa && key is EdDsaSecurityKey edDsaKey
            ? new NSecEdDsaSignatureProvider(edDsaKey, algorithm)
            : base.CreateForSigning(key, algorithm);
}
