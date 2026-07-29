using System.Security.Cryptography;
using System.Text;
using Backoffice.Application.Documents;

namespace Backoffice.Infrastructure.Documents;

/// <summary>
/// Durable filesystem implementation backed by a shared volume. References are opaque and
/// validated before path resolution so caller-controlled names cannot escape the store.
/// </summary>
public sealed class FileSystemDocumentStorage(DocumentStorageOptions options) : IDocumentStorage
{
    private const string Scheme = "document-store";
    private static readonly char[] InvalidFileNameCharacters = Path.GetInvalidFileNameChars();
    private readonly string _rootPath = Path.GetFullPath(options.RootPath);

    public async Task<StoredDocument> StoreQuarantinedAsync(
        string tenantId,
        Guid caseId,
        string fileName,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default)
    {
        if (content.IsEmpty)
        {
            throw new ArgumentException("Document content cannot be empty.", nameof(content));
        }

        if (content.Length > options.MaxUploadBytes)
        {
            throw new ArgumentException(
                $"Document exceeds the configured {options.MaxUploadBytes}-byte upload limit.",
                nameof(content));
        }

        var tenantHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(tenantId)));
        var safeFileName = SanitizeFileName(fileName);
        var relativePath = Path.Combine(
            tenantHash,
            caseId.ToString("N"),
            Guid.NewGuid().ToString("N"),
            safeFileName);
        var storageReference = BuildReference("quarantine", relativePath);
        var targetPath = ResolvePath(storageReference, expectedZone: "quarantine");

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        await File.WriteAllBytesAsync(targetPath, content.ToArray(), cancellationToken);

        var checksum = Convert.ToHexStringLower(SHA256.HashData(content.Span));
        return new StoredDocument(storageReference, checksum);
    }

    public async Task<StoredDocumentContent> ReadAsync(
        string storageReference,
        CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(storageReference);
        var content = await File.ReadAllBytesAsync(path, cancellationToken);
        return new StoredDocumentContent(content, Path.GetFileName(path));
    }

    public async Task<string> PromoteAsync(
        string quarantineReference,
        CancellationToken cancellationToken = default)
    {
        var sourcePath = ResolvePath(quarantineReference, expectedZone: "quarantine");
        var sourceUri = ParseReference(quarantineReference, expectedZone: "quarantine");
        var relativePath = Uri.UnescapeDataString(sourceUri.AbsolutePath.TrimStart('/'));
        var acceptedReference = BuildReference("accepted", relativePath);
        var destinationPath = ResolvePath(acceptedReference, expectedZone: "accepted");

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        if (!File.Exists(destinationPath))
        {
            await using var source = File.OpenRead(sourcePath);
            await using var destination = new FileStream(
                destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await source.CopyToAsync(destination, cancellationToken);
        }
        else
        {
            var sourceChecksum = await ComputeChecksumAsync(sourcePath, cancellationToken);
            var destinationChecksum = await ComputeChecksumAsync(destinationPath, cancellationToken);
            if (!string.Equals(sourceChecksum, destinationChecksum, StringComparison.Ordinal))
            {
                throw new IOException("Accepted document already exists with different content.");
            }
        }

        return acceptedReference;
    }

    private static string SanitizeFileName(string fileName)
    {
        var baseName = Path.GetFileName(fileName);
        var safeCharacters = baseName
            .Select(character =>
                InvalidFileNameCharacters.Contains(character)
                || character is '/' or '\\' or ':' or '\0'
                    ? '_'
                    : character)
            .ToArray();
        var sanitized = new string(safeCharacters).Trim();

        if (string.IsNullOrWhiteSpace(sanitized) || sanitized is "." or "..")
        {
            sanitized = "document.bin";
        }

        return sanitized.Length <= 180 ? sanitized : sanitized[..180];
    }

    private static string BuildReference(string zone, string relativePath)
    {
        var segments = relativePath
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.EscapeDataString);
        return $"{Scheme}://{zone}/{string.Join('/', segments)}";
    }

    private string ResolvePath(string storageReference, string? expectedZone = null)
    {
        var uri = ParseReference(storageReference, expectedZone);
        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString)
            .ToArray();

        if (segments.Length != 4
            || !IsLowerHex(segments[0], 64)
            || !IsLowerHex(segments[1], 32)
            || !IsLowerHex(segments[2], 32)
            || segments[3] != SanitizeFileName(segments[3]))
        {
            throw new InvalidOperationException("Invalid document storage reference.");
        }

        var zoneRoot = Path.GetFullPath(Path.Combine(_rootPath, uri.Host));
        var fullPath = Path.GetFullPath(Path.Combine([zoneRoot, .. segments]));
        if (!fullPath.StartsWith(zoneRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Document storage reference escapes its zone.");
        }

        return fullPath;
    }

    private static Uri ParseReference(string storageReference, string? expectedZone)
    {
        if (!Uri.TryCreate(storageReference, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Scheme, StringComparison.Ordinal)
            || uri.Host is not ("quarantine" or "accepted")
            || (expectedZone is not null && !string.Equals(uri.Host, expectedZone, StringComparison.Ordinal))
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException("Invalid document storage reference.");
        }

        return uri;
    }

    private static bool IsLowerHex(string value, int length) =>
        value.Length == length && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static async Task<string> ComputeChecksumAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var checksum = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(checksum);
    }
}
