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

    internal Func<LayerConventionProfile, LayerInterfacePairingConvention>? ResolveInterfacePairing { get; init; }
    internal Func<IReadOnlyList<GeneratedFile>, Dictionary<string, string>>? BuildProposedTypeDefinitions { get; init; }
    internal Func<string, IReadOnlyList<GeneratedFile>, Dictionary<string, HashSet<string>>>? BuildInterfaceMemberCatalog { get; init; }

    private Dictionary<string, string>? _proposedTypeDefinitions;
    private Dictionary<string, HashSet<string>>? _interfaceMemberCatalog;
    private Dictionary<string, LayerInterfacePairingConvention>? _interfacePairingConventions;

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

    public LayerInterfacePairingConvention GetInterfacePairingConvention(LayerConventionProfile profile)
    {
        _interfacePairingConventions ??= new Dictionary<string, LayerInterfacePairingConvention>(StringComparer.OrdinalIgnoreCase);
        string cacheKey = $"{profile.RoleName}|{profile.FileSuffix}";
        if (_interfacePairingConventions.TryGetValue(cacheKey, out LayerInterfacePairingConvention cached))
        {
            return cached;
        }

        LayerInterfacePairingConvention convention = profile.SampleCount > 0
            ? profile.InterfacePairing
            : ResolveInterfacePairing?.Invoke(profile) ?? profile.InterfacePairing;
        _interfacePairingConventions[cacheKey] = convention;
        return convention;
    }

    public Dictionary<string, string> GetProposedTypeDefinitions() =>
        _proposedTypeDefinitions ??= BuildProposedTypeDefinitions?.Invoke(ProposedFiles)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);

    public Dictionary<string, HashSet<string>> GetInterfaceMemberCatalog() =>
        _interfaceMemberCatalog ??= BuildInterfaceMemberCatalog?.Invoke(RepoPath, ProposedFiles)
            ?? new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
}
