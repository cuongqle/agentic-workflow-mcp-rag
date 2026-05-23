using agents_mcp_rag.Infrastructure;

sealed class ComplianceContext
{
    public WorkflowState State { get; }
    public string RepoPath { get; }
    public RepoContract Contract { get; }
    public RepoStack Stack { get; }
    public IReadOnlyList<GeneratedFile> ProposedFiles { get; }
    public IReadOnlySet<string> ProposedPaths { get; }

    private Dictionary<string, string>? _proposedTypeDefinitions;
    private Dictionary<string, HashSet<string>>? _interfaceMemberCatalog;
    private Dictionary<string, LayerInterfacePairingConvention>? _interfacePairingConventions;

    private ComplianceContext(
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

    public static ComplianceContext Create(WorkflowState state)
    {
        var proposedFiles = WorkflowFindingRules.GetAllProposedFiles(state);
        var proposedPaths = new HashSet<string>(
            proposedFiles.Select(f => f.RelativePath.Replace('\\', '/')),
            StringComparer.OrdinalIgnoreCase);
        RepoContract contract = state.Contract ?? RepoContractDiscoverer.Discover(state.RepoPath);

        return new ComplianceContext(state, state.RepoPath, contract, proposedFiles, proposedPaths);
    }

    public LayerInterfacePairingConvention GetInterfacePairingConvention(LayerConventionProfile profile)
    {
        _interfacePairingConventions ??= new Dictionary<string, LayerInterfacePairingConvention>(StringComparer.OrdinalIgnoreCase);
        string cacheKey = $"{profile.RoleName}|{profile.FileSuffix}";
        if (_interfacePairingConventions.ContainsKey(cacheKey))
        {
            return _interfacePairingConventions[cacheKey];
        }

        LayerInterfacePairingConvention convention = profile.SampleCount > 0
            ? profile.InterfacePairing
            : LayerInterfacePairingDiscoverer.Discover(RepoPath, profile);
        _interfacePairingConventions[cacheKey] = convention;
        return convention;
    }

    public Dictionary<string, string> GetProposedTypeDefinitions() =>
        _proposedTypeDefinitions ??= Stack.DotNetOr(
            TypeMemberConsistencyGuard.BuildProposedTypeDefinitions(ProposedFiles),
            new Dictionary<string, string>(StringComparer.Ordinal));

    public Dictionary<string, HashSet<string>> GetInterfaceMemberCatalog() =>
        _interfaceMemberCatalog ??= Stack.DotNetOr(
            InterfaceImplementationGuard.BuildDirectMemberCatalog(RepoPath, ProposedFiles),
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal));
}
