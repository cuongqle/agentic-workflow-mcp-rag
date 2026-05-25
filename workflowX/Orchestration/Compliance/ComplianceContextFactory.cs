using workflowX.Infrastructure.Compliance.DotNet;

namespace workflowX.Orchestration.Compliance;

static class ComplianceContextFactory
{
    public static ComplianceContext Create(WorkflowState state)
    {
        var proposedFiles = ProposedFileSupport.GetFilesForComplianceValidation(state);
        var proposedPaths = new HashSet<string>(
            proposedFiles.Select(f => f.RelativePath.Replace('\\', '/')),
            StringComparer.OrdinalIgnoreCase);
        RepoContract contract = state.Contract ?? RepoContractDiscoverer.Discover(state.RepoPath);
        RepoStack stack = contract.Stack;

        return new ComplianceContext(state, state.RepoPath, contract, proposedFiles, proposedPaths)
        {
            ResolveInterfacePairing = profile =>
                stack.DotNetOr(
                    profile.SampleCount > 0
                        ? profile.InterfacePairing
                        : LayerInterfacePairingDiscoverer.Discover(state.RepoPath, profile),
                    LayerInterfacePairingConvention.None),
            BuildProposedTypeDefinitions = files =>
                stack.DotNetOr(
                    TypeMemberConsistencyGuard.BuildProposedTypeDefinitions(files),
                    new Dictionary<string, string>(StringComparer.Ordinal)),
            BuildInterfaceMemberCatalog = (repoPath, files) =>
                stack.DotNetOr(
                    InterfaceImplementationGuard.BuildDirectMemberCatalog(repoPath, files),
                    new Dictionary<string, HashSet<string>>(StringComparer.Ordinal))
        };
    }
}
