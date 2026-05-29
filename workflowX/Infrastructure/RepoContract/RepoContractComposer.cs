namespace workflowX.Infrastructure;

/// <summary>
/// Runs stack-specific repo discovery and merges signals into <see cref="RepoContract"/>.
/// Add a new stack by: (1) <c>*RepoContractDiscoverer</c> under <c>RepoContract/DotNet/</c> or parent,
/// (2) extend <see cref="RepoContractDiscovery"/>, (3) merge in <see cref="Compose"/>.
/// </summary>
internal static class RepoContractComposer
{
    internal static RepoContractDiscovery Scan(string repoPath) =>
        new(
            Frontend: FrontendRepoContractDiscoverer.Discover(repoPath),
            Entity: DotNetRepoContractDiscoverer.DiscoverEntityConvention(repoPath));

    internal static RepoContract Compose(string repoPath, RepoContractDiscovery discovery, bool hasDotNetProjects) =>
        new()
        {
            RepoPath = repoPath,
            Frontend = discovery.Frontend.ModuleTemplate,
            Entity = discovery.Entity,
            HasDotNetProjects = hasDotNetProjects,
        };
}

/// <summary>Per-stack discovery results before merge. Extend when adding Python, Go, etc.</summary>
internal sealed record RepoContractDiscovery(
    FrontendRepoContractSignals Frontend,
    EntityConvention? Entity);
