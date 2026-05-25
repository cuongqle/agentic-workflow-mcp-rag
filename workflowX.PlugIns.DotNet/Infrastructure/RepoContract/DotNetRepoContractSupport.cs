namespace workflowX.Infrastructure;

internal static class DotNetRepoContractSupport
{
    internal static RepoContract GetContract(WorkflowState state)
    {
        if (state.Contract is not null)
        {
            return state.Contract;
        }

        DotNetRepoContractSignals signals = DotNetRepoContractDiscoverer.Discover(state.RepoPath);
        return new RepoContract
        {
            RepoPath = state.RepoPath,
            PathRules = signals.PathRules,
            LayerConventions = signals.LayerConventions,
            Entity = signals.Entity,
            RepositoryInterfacesNamespace = signals.RepositoryInterfacesNamespace,
            ConsumerSuffixes = signals.ConsumerSuffixes,
            CompositionRootPaths = signals.CompositionRootPaths,
            RegistrationScope = signals.RegistrationScope
        };
    }

    internal static string ResolveCanonicalRelativePath(RepoContract contract, string relativePath, string content)
    {
        relativePath = RemapMisplacedRepositoryImplementation(relativePath, contract);
        return contract.ResolveCanonicalRelativePath(relativePath, content);
    }

    private static string RemapMisplacedRepositoryImplementation(string relativePath, RepoContract contract)
    {
        string normalized = relativePath.Replace('\\', '/').TrimStart('/');
        string fileName = Path.GetFileName(normalized);
        if (!DotNetRepoContractDiscoverer.IsRepositoryImplementationFileName(fileName))
        {
            return relativePath;
        }

        string? directory = Path.GetDirectoryName(normalized)?.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(directory)
            || !DotNetRepoContractDiscoverer.IsUnderInterfacesDirectory(directory))
        {
            return relativePath;
        }

        PathPlacementRule? implementationRule = contract.PathRules.FirstOrDefault(rule =>
            rule.FileSuffix.Equals("Repository.cs", StringComparison.OrdinalIgnoreCase)
            && rule.FileFilter is not null
            && rule.FileFilter(fileName));

        return implementationRule is null
            ? relativePath
            : $"{implementationRule.Directory}/{fileName}";
    }
}
