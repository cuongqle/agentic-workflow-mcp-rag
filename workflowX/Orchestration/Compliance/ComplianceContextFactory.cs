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
        return new ComplianceContext(state, state.RepoPath, contract, proposedFiles, proposedPaths);
    }
}
