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
}
