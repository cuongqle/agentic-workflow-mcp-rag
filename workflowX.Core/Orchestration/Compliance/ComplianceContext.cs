using workflowX.Infrastructure;

namespace workflowX.Orchestration.Compliance;

public sealed class ComplianceContext
{
    public WorkflowState State { get; }
    public string RepoPath { get; }
    public RepoContract Contract { get; }
    public RepoStack Stack { get; }
    public IReadOnlyList<GeneratedFile> ProposedFiles { get; }
    public IReadOnlySet<string> ProposedPaths { get; }

    internal ComplianceContext(
        WorkflowState state,
        string repoPath,
        RepoContract contract,
        IReadOnlyList<GeneratedFile> proposedFiles,
        IReadOnlySet<string> proposedPaths)
    {
        State = state;
        RepoPath = repoPath;
        Contract = contract;
        Stack = contract.Stack;
        ProposedFiles = proposedFiles;
        ProposedPaths = proposedPaths;
    }
}
