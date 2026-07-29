using System.Security.Cryptography;
using Backoffice.Infrastructure.Documents;

namespace Backoffice.Api.Tests;

public sealed class FileSystemDocumentStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "backoffice-storage-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task StoreReadAndPromote_UsesOpaqueValidatedReferences()
    {
        var storage = new FileSystemDocumentStorage(new DocumentStorageOptions
        {
            RootPath = _root,
            MaxUploadBytes = 1024,
        });
        var content = "document content"u8.ToArray();

        var stored = await storage.StoreQuarantinedAsync(
            "secret-tenant", Guid.NewGuid(), "../receipt:2026.pdf", content);

        Assert.StartsWith("document-store://quarantine/", stored.StorageReference);
        Assert.DoesNotContain("secret-tenant", stored.StorageReference, StringComparison.Ordinal);
        Assert.DoesNotContain("..", stored.StorageReference, StringComparison.Ordinal);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(content)), stored.Checksum);
        var quarantined = await storage.ReadAsync(stored.StorageReference);
        Assert.Equal(content, quarantined.Content);
        Assert.Equal("receipt_2026.pdf", quarantined.FileName);

        var acceptedReference = await storage.PromoteAsync(stored.StorageReference);
        Assert.StartsWith("document-store://accepted/", acceptedReference);
        Assert.Equal(content, (await storage.ReadAsync(acceptedReference)).Content);

        // A retry after a crash between copying and committing is safe.
        Assert.Equal(acceptedReference, await storage.PromoteAsync(stored.StorageReference));
    }

    [Fact]
    public async Task Read_RejectsReferenceOutsideManagedLayout()
    {
        var storage = new FileSystemDocumentStorage(new DocumentStorageOptions { RootPath = _root });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            storage.ReadAsync("file:///etc/passwd"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            storage.ReadAsync($"document-store://quarantine/{new string('a', 64)}/../escape"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
