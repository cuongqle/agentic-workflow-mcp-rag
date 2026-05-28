namespace workflowX.Infrastructure;

/// <summary>
/// Discovers <see cref="RepoContract"/> with lightweight stack detection (no layer/path heuristics).
/// </summary>
internal static class RepoContractDiscoverer
{
    public static RepoContract Discover(string repoPath)
    {
        if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
        {
            return new RepoContract { RepoPath = repoPath ?? string.Empty };
        }

        RepoContractDiscovery discovery = RepoContractComposer.Scan(repoPath);
        return RepoContractComposer.Compose(repoPath, discovery, HasDotNetProjects(repoPath));
    }

    private static bool HasDotNetProjects(string repoPath) =>
        Directory
            .EnumerateFiles(repoPath, "*.csproj", SearchOption.AllDirectories)
            .Any(path => !IsArtifactPath(path));

    private static bool IsArtifactPath(string path) =>
        path.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
        || path.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
        || path.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase)
        || path.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase);
}
