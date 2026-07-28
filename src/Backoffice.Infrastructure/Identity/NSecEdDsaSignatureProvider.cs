using Microsoft.IdentityModel.Tokens;
using NSec.Cryptography;

namespace Backoffice.Infrastructure.Identity;

/// <summary>
/// Signs/verifies with Ed25519 via NSec.Cryptography (a libsodium wrapper — .NET's own
/// `ECDsa`/`RSA` types don't implement Ed25519), plugged into the standard
/// `Microsoft.IdentityModel.Tokens` signature-provider pipeline so `JsonWebTokenHandler`
/// can validate "EdDSA" tokens without any other custom JWT-parsing code (task 11.1).
/// </summary>
public sealed class NSecEdDsaSignatureProvider(EdDsaSecurityKey key, string algorithm) : SignatureProvider(key, algorithm)
{
    private static readonly SignatureAlgorithm Ed25519Algorithm = SignatureAlgorithm.Ed25519;

    public override byte[] Sign(byte[] input)
    {
        if (key.PrivateKey is null)
        {
            throw new InvalidOperationException("This EdDsaSecurityKey has no private key material to sign with.");
        }

        return Ed25519Algorithm.Sign(key.PrivateKey, input);
    }

    // Newer Microsoft.IdentityModel.Tokens versions call this span-based overload directly
    // (its base implementation throws NotImplementedException rather than delegating to the
    // byte[]-based Sign above), so both must be implemented.
    public override bool Sign(ReadOnlySpan<byte> data, Span<byte> destination, out int bytesWritten)
    {
        if (key.PrivateKey is null)
        {
            throw new InvalidOperationException("This EdDsaSecurityKey has no private key material to sign with.");
        }

        var signature = Ed25519Algorithm.Sign(key.PrivateKey, data);
        signature.CopyTo(destination);
        bytesWritten = signature.Length;
        return true;
    }

    public override bool Verify(byte[] input, byte[] signature) => Ed25519Algorithm.Verify(key.PublicKey, input, signature);

    protected override void Dispose(bool disposing)
    {
    }
}
