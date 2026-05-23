using agents_mcp_rag.Infrastructure.Compliance.DotNet;

using agents_mcp_rag.Infrastructure.Compliance.DotNet;

namespace agents_mcp_rag.Orchestration.Compliance;

static class ComplianceContextFactory
{
    public static ComplianceContext Create(WorkflowState state)
    {
        var proposedFiles = ProposedFileSupport.GetAllProposedFiles(state);
        var proposedPaths = new HashSet<string>(
            proposedFiles.Select(f => f.RelativePath.Replace('\\', '/')),
            StringComparer.OrdinalIgnoreCase);
        RepoContract contract = state.Contract ?? RepoContractDiscoverer.Discover(state.RepoPath);
        RepoStack stack = contract.Stack;

        return new ComplianceContext(state, state.RepoPath, contract, proposedFiles, proposedPaths)
        {
            ResolveInterfacePairing = profile =>
                profile.SampleCount > 0
                    ? profile.InterfacePairing
                    : LayerInterfacePairingDiscoverer.Discover(state.RepoPath, profile),
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
