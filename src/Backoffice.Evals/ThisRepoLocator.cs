namespace Backoffice.Evals;

/// <summary>
/// Locates this repo's own root (marked by Backoffice.sln) — for artifacts this repo owns
/// itself, like evals/document-analysis-v1.jsonl, as opposed to
/// <see cref="ArchitectureRepoLocator"/>'s sibling-repo artifacts.
/// </summary>
public static class ThisRepoLocator
{
    public static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Backoffice.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException($"Could not locate Backoffice.sln above '{AppContext.BaseDirectory}'.");
    }
}
