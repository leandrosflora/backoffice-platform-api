using Microsoft.IdentityModel.Tokens;
using NSec.Cryptography;

namespace Backoffice.Infrastructure.Identity;

/// <summary>
/// Wraps an NSec Ed25519 key pair (or public key alone, for verification-only use) as a
/// `Microsoft.IdentityModel.Tokens.SecurityKey`, so the standard `JsonWebTokenHandler` can
/// validate/sign "EdDSA" tokens through it. .NET's built-in `ECDsa`/`RSA` security-key types
/// don't support Ed25519 (a distinct Edwards-curve scheme, not a NIST curve), which is why
/// design.md calls for NSec.Cryptography here (task 11.1).
/// </summary>
public sealed class EdDsaSecurityKey : AsymmetricSecurityKey
{
    public Key? PrivateKey { get; }
    public PublicKey PublicKey { get; }

    public EdDsaSecurityKey(PublicKey publicKey)
    {
        PublicKey = publicKey;
        CryptoProviderFactory = new EdDsaCryptoProviderFactory();
        KeyId = ComputeKeyId(publicKey);
    }

    public EdDsaSecurityKey(Key privateKey)
    {
        PrivateKey = privateKey;
        PublicKey = privateKey.PublicKey;
        CryptoProviderFactory = new EdDsaCryptoProviderFactory();
        KeyId = ComputeKeyId(PublicKey);
    }

    // Some Microsoft.IdentityModel.Tokens key-resolution paths behave differently when
    // every candidate key has an empty KeyId — deriving one deterministically from the
    // public key bytes means the signing-side and verifying-side EdDsaSecurityKey
    // instances (constructed separately, from the private key and from the public key PEM
    // respectively) always agree on it, since both resolve to the same underlying key.
    private static string ComputeKeyId(PublicKey publicKey) =>
        Convert.ToHexString(publicKey.Export(KeyBlobFormat.RawPublicKey))[..16];

    public override int KeySize => 256;

    public override bool IsSupportedAlgorithm(string algorithm) => algorithm == SecurityAlgorithms.EdDsa;

    public override PrivateKeyStatus PrivateKeyStatus => PrivateKey is not null ? PrivateKeyStatus.Exists : PrivateKeyStatus.DoesNotExist;

    // Abstract base member — must override despite being marked obsolete; PrivateKeyStatus
    // above is the real, non-obsolete accessor callers should use instead.
    [Obsolete("Use PrivateKeyStatus instead.")]
#pragma warning disable CS0618
    public override bool HasPrivateKey => PrivateKey is not null;
#pragma warning restore CS0618
}

/// <summary>Standard algorithm identifier for EdDSA/Ed25519, matching the JWT `alg` header
/// value the Python reference and docs/security/workload-identity.md both use.</summary>
public static class SecurityAlgorithms
{
    public const string EdDsa = "EdDSA";
}
