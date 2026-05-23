using agents_mcp_rag.Infrastructure.CodeApply.DotNet;

namespace agents_mcp_rag.Infrastructure;

static class ApplyContextFactory
{
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
