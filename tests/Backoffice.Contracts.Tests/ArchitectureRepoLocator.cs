namespace Backoffice.Contracts.Tests;

/// <summary>
/// Locates the sibling `intelligent-backoffice-platform-architecture` repo that holds the
/// authoritative OpenAPI/AsyncAPI/JSON-Schema contracts this codebase implements against.
/// Walks up from the test binary's directory since CI/local layouts vary.
/// </summary>
public static class ArchitectureRepoLocator
{
    private const string RepoDirectoryName = "intelligent-backoffice-platform-architecture";

    public static string FindContractsRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, RepoDirectoryName, "contracts");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate a sibling '{RepoDirectoryName}/contracts' directory above '{AppContext.BaseDirectory}'.");
    }
}
