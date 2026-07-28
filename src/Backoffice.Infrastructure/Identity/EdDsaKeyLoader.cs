using NSec.Cryptography;

namespace Backoffice.Infrastructure.Identity;

/// <summary>
/// Reads the PEM key files `scripts/generate_dev_identity.py` (and its .NET equivalent,
/// <see cref="DevIdentityGenerator"/>) produce — PKCS8 for the private key, SubjectPublicKeyInfo
/// for the public key — via NSec's PKIX blob formats, so either script's output is
/// interchangeable (task 11.5).
/// </summary>
public static class EdDsaKeyLoader
{
    public static Key LoadPrivateKey(string pemPath) =>
        Key.Import(
            SignatureAlgorithm.Ed25519,
            File.ReadAllBytes(pemPath),
            KeyBlobFormat.PkixPrivateKeyText,
            new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });

    public static PublicKey LoadPublicKey(string pemPath) =>
        PublicKey.Import(SignatureAlgorithm.Ed25519, File.ReadAllBytes(pemPath), KeyBlobFormat.PkixPublicKeyText);
}
