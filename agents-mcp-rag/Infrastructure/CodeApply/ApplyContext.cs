namespace agents_mcp_rag.Infrastructure;

/// <summary>
/// Shared snapshot for a single apply pass — catalogs and paths computed once per run.
/// </summary>
internal sealed class ApplyContext
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

    private ApplyContext(
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

    public static ApplyContext Create(WorkflowState state)
    {
        string repoPath = state.RepoPath;
        string repoRoot = Path.GetFullPath(repoPath);
        RepoContract contract = state.Contract ?? RepoContractDiscoverer.Discover(repoPath);
        RepoStack stack = contract.Stack;
        IReadOnlyList<GeneratedFile> generatedFiles = GeneratedFileApplier.GetOrderedGeneratedFiles(state);
        var workflowProposedPaths = new HashSet<string>(
            generatedFiles.Select(f => f.RelativePath.Replace('\\', '/')),
            StringComparer.OrdinalIgnoreCase);

        return stack.DotNetOr(
            new ApplyContext(
                state,
                repoPath,
                repoRoot,
                contract,
                stack,
                generatedFiles,
                workflowProposedPaths,
                InterfaceImplementationGuard.BuildDirectMemberCatalog(repoPath, generatedFiles),
                CSharpApplySupport.BuildInterfaceCatalog(repoPath, generatedFiles),
                CSharpApplySupport.BuildTypeNamespaceCatalog(repoPath, generatedFiles),
                TypeMemberConsistencyGuard.BuildProposedTypeDefinitions(generatedFiles)),
            new ApplyContext(
                state,
                repoPath,
                repoRoot,
                contract,
                stack,
                generatedFiles,
                workflowProposedPaths,
                new Dictionary<string, HashSet<string>>(StringComparer.Ordinal),
                new InterfaceCatalog(new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)),
                new TypeNamespaceCatalog(new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)),
                new Dictionary<string, string>(StringComparer.Ordinal)));
    }
}
