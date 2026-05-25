using workflowX.Configuration;
using workflowX.Infrastructure;

/// <summary>
/// Prepares recovery/compilation-fix context for the RecoveryAgent, routed by <see cref="RepoStack"/>.
/// </summary>
static class RecoveryContextSupport
{
    public static void Prepare(
        WorkflowState state,
        string timelineLabel,
        CompilationFixContextOptions options,
        Action<string> addTimeline)
    {
        RepoStack stack = RepoStack.From(state);
        stack.WhenDotNet(
            () => PrepareDotNetBackend(state, timelineLabel, options, addTimeline),
            () => PrepareFromProposedFiles(state));
    }

    private static void PrepareFromProposedFiles(WorkflowState state)
    {
        state.CompilationFixAllowedFiles = WorkflowFindingRules.GetAllProposedFiles(state)
            .Select(f => f.RelativePath.Replace('\\', '/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        state.CompilationFixExemplarContext = string.Empty;
    }

    private static void PrepareDotNetBackend(
        WorkflowState state,
        string timelineLabel,
        CompilationFixContextOptions options,
        Action<string> addTimeline)
    {
        foreach (string entry in CSharpCompilationFixSupport.PrepareRecoveryPass(state))
        {
            addTimeline(entry);
        }

        state.CompilationFixAllowedFiles = CSharpCompilationFixSupport.DetermineAllowedFiles(state);
        var (exemplarContext, attachedPaths, omittedPaths) =
            CSharpCompilationFixSupport.BuildContext(state, options);
        state.CompilationFixExemplarContext = exemplarContext;

        if (attachedPaths.Count > 0)
        {
            addTimeline(
                $"{timelineLabel}: inlined {attachedPaths.Count} full source file(s): {string.Join(", ", attachedPaths)}");
        }

        if (omittedPaths.Count > 0)
        {
            addTimeline(
                $"{timelineLabel}: {omittedPaths.Count} lower-priority path(s) not inlined "
                + $"(raise Workflow.CompilationFixMaxContextChars or set to 0 for unlimited): "
                + $"{string.Join(", ", omittedPaths.Take(12))}"
                + (omittedPaths.Count > 12 ? ", ..." : string.Empty));
        }
    }
}
