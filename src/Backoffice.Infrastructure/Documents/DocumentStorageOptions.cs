namespace Backoffice.Infrastructure.Documents;

public sealed class DocumentStorageOptions
{
    public const long DefaultMaxUploadBytes = 10 * 1024 * 1024;

    public string RootPath { get; init; } = ".local/document-storage";
    public long MaxUploadBytes { get; init; } = DefaultMaxUploadBytes;
}
