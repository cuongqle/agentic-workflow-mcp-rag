namespace agents_mcp_rag.Infrastructure;

/// <summary>
/// Runs stack-specific repo discovery and merges signals into <see cref="RepoContract"/>.
/// Add a new stack by: (1) <c>*RepoContractDiscoverer</c> under <c>RepoContract/DotNet/</c> or parent,
/// (2) extend <see cref="RepoContractDiscovery"/>, (3) merge in <see cref="Compose"/>.
/// </summary>
internal static class RepoContractComposer
{
    internal static RepoContractDiscovery Scan(string repoPath) =>
        new(
            DotNet: DotNetRepoContractDiscoverer.Discover(repoPath),
            Frontend: FrontendRepoContractDiscoverer.Discover(repoPath));

    internal static RepoContract Compose(string repoPath, RepoContractDiscovery discovery) =>
        new()
        {
            RepoPath = repoPath,
            PathRules = discovery.DotNet.PathRules,
            Frontend = discovery.Frontend.ModuleTemplate,
            LayerConventions = discovery.DotNet.LayerConventions,
            Entity = discovery.DotNet.Entity,
            RepositoryInterfacesNamespace = discovery.DotNet.RepositoryInterfacesNamespace,
            ConsumerSuffixes = discovery.DotNet.ConsumerSuffixes,
            CompositionRootPaths = discovery.DotNet.CompositionRootPaths,
            RegistrationScope = discovery.DotNet.RegistrationScope
        };
}

/// <summary>Per-stack discovery results before merge. Extend when adding Python, Go, etc.</summary>
internal sealed record RepoContractDiscovery(
    DotNetRepoContractSignals DotNet,
    FrontendRepoContractSignals Frontend);
