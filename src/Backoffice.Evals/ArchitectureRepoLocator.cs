namespace Backoffice.Evals;

/// <summary>
/// Locates the sibling intelligent-backoffice-platform-architecture repo that owns
/// evals/datasets/intelligence-v1.jsonl and evals/thresholds.yaml.
/// </summary>
public static class ArchitectureRepoLocator
{
    private const string RepoDirectoryName = "intelligent-backoffice-platform-architecture";

    public static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, RepoDirectoryName, "evals");
            if (Directory.Exists(candidate))
            {
                return Path.Combine(dir.FullName, RepoDirectoryName);
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate a sibling '{RepoDirectoryName}/evals' directory above '{AppContext.BaseDirectory}'.");
    }
}
