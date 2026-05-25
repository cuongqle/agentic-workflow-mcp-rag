namespace workflowX.Infrastructure;

/// <summary>
/// Discovers <see cref="RepoContract"/> by scanning each supported stack independently, then merging.
/// </summary>
internal static class RepoContractDiscoverer
{
    public static RepoContract Discover(string repoPath)
    {
        if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
        {
            return new RepoContract
            {
                RepoPath = repoPath ?? string.Empty,
                LayerConventions = LayerConventionProfiles.Empty
            };
        }

        RepoContractDiscovery discovery = RepoContractComposer.Scan(repoPath);
        return RepoContractComposer.Compose(repoPath, discovery);
    }
}
