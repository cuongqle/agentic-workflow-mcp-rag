namespace agents_mcp_rag.Infrastructure;

/// <summary>
/// Shared snapshot for a single apply pass — catalogs and paths computed once per run.
/// </summary>
public sealed class ApplyContext
{
    public WorkflowState State { get; }
    public string RepoPath { get; }
    public string RepoRoot { get; }
    public RepoContract Contract { get; }
    public RepoStack Stack { get; }
    public LayerConventionProfiles LayerConventions { get; }
    public IReadOnlyList<GeneratedFile> GeneratedFiles { get; }
    public IReadOnlySet<string> WorkflowProposedPaths { get; }
    public Dictionary<string, HashSet<string>> InterfaceDirectMembers { get; }
    public InterfaceCatalog InterfaceCatalog { get; }
    public TypeNamespaceCatalog TypeNamespaceCatalog { get; }
    public Dictionary<string, string> ProposedTypeDefinitions { get; }
    public Dictionary<string, string> DeclaredTypePaths { get; } = new(StringComparer.Ordinal);

    internal ApplyContext(
        WorkflowState state,
        string repoPath,
        string repoRoot,
        RepoContract contract,
        RepoStack stack,
        IReadOnlyList<GeneratedFile> generatedFiles,
        IReadOnlySet<string> workflowProposedPaths,
        Dictionary<string, HashSet<string>> interfaceDirectMembers,
        InterfaceCatalog interfaceCatalog,
        TypeNamespaceCatalog typeNamespaceCatalog,
        Dictionary<string, string> proposedTypeDefinitions)
    {
        State = state;
        RepoPath = repoPath;
        RepoRoot = repoRoot;
        Contract = contract;
        Stack = stack;
        LayerConventions = contract.LayerConventions;
        GeneratedFiles = generatedFiles;
        WorkflowProposedPaths = workflowProposedPaths;
        InterfaceDirectMembers = interfaceDirectMembers;
        InterfaceCatalog = interfaceCatalog;
        TypeNamespaceCatalog = typeNamespaceCatalog;
        ProposedTypeDefinitions = proposedTypeDefinitions;
    }
}
