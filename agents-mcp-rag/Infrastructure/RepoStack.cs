namespace agents_mcp_rag.Infrastructure;

/// <summary>
/// Discovered repo stacks — single snapshot used across apply, RAG, compliance, and recovery.
/// Source of truth remains <see cref="RepoContract.HasDotNetBackend"/> / <see cref="RepoContract.HasFrontend"/>.
/// </summary>
readonly record struct RepoStack(bool DotNet, bool Frontend)
{
    public static RepoStack None => new(false, false);

    public static RepoStack From(RepoContract contract) =>
        new(contract.HasDotNetBackend, contract.HasFrontend);

    public static RepoStack From(WorkflowState state) =>
        state.Contract is not null
            ? From(state.Contract)
            : InferFromRagContext(state);

    public void WhenDotNet(Action action)
    {
        if (DotNet)
        {
            action();
        }
    }

    public void WhenDotNet(Action dotNet, Action otherwise)
    {
        if (DotNet)
        {
            dotNet();
        }
        else
        {
            otherwise();
        }
    }

    public void WhenFrontend(Action action)
    {
        if (Frontend)
        {
            action();
        }
    }

    public IEnumerable<T> WhenDotNet<T>(IEnumerable<T> items) =>
        DotNet ? items : Enumerable.Empty<T>();

    public IEnumerable<T> WhenFrontend<T>(IEnumerable<T> items) =>
        Frontend ? items : Enumerable.Empty<T>();

    public T DotNetOr<T>(T whenDotNet, T otherwise) =>
        DotNet ? whenDotNet : otherwise;

    private static RepoStack InferFromRagContext(WorkflowState state)
    {
        string structureContext = $"{state.ProjectStructureContext}\n{state.CombinedRagContext}";
        bool frontend = structureContext.Contains("Frontend host:", StringComparison.OrdinalIgnoreCase)
                        || structureContext.Contains("Feature modules root:", StringComparison.OrdinalIgnoreCase);
        bool dotNet = structureContext.Contains("Layer '", StringComparison.OrdinalIgnoreCase)
                      || structureContext.Contains("Entities:", StringComparison.OrdinalIgnoreCase)
                      || structureContext.Contains("Repository interfaces namespace:", StringComparison.OrdinalIgnoreCase);
        return new RepoStack(dotNet, frontend);
    }
}
