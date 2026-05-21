using agents_mcp_rag.Infrastructure;

sealed partial class WorkflowOrchestrator
{
    private void PrepareRecoveryContext(WorkflowState state, string timelineLabel)
    {
        foreach (string entry in CompilationFixFileResolver.PrepareRecoveryPass(state))
        {
            state.AddTimeline(entry);
        }

        state.CompilationFixAllowedFiles = CompilationFixFileResolver.DetermineAllowedFiles(state);
        var (exemplarContext, attachedPaths, omittedPaths) =
            CompilationFixContextBuilder.Build(state, _compilationFixContextOptions);
        state.CompilationFixExemplarContext = exemplarContext;
        if (attachedPaths.Count > 0)
        {
            state.AddTimeline(
                $"{timelineLabel}: inlined {attachedPaths.Count} full source file(s): {string.Join(", ", attachedPaths)}");
        }

        if (omittedPaths.Count > 0)
        {
            state.AddTimeline(
                $"{timelineLabel}: {omittedPaths.Count} lower-priority path(s) not inlined "
                + $"(raise Workflow.CompilationFixMaxContextChars or set to 0 for unlimited): "
                + $"{string.Join(", ", omittedPaths.Take(12))}"
                + (omittedPaths.Count > 12 ? ", ..." : string.Empty));
        }

    }

    private static void RecordNuGetPackageChanges(WorkflowState state)
    {
        foreach (string packageChange in ProjectPackageAuditor.EnsureMissingPackages(
                     state.RepoPath,
                     state.BuildValidation?.Findings,
                     WorkflowFindingRules.GetAllProposedFiles(state)))
        {
            state.AddTimeline($"NuGet restore: {packageChange}");
        }
    }
}
