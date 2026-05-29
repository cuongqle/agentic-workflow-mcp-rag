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
            () => PrepareFromProposedFiles(state, timelineLabel, addTimeline));
    }

    private static void PrepareFromProposedFiles(
        WorkflowState state,
        string timelineLabel,
        Action<string> addTimeline)
    {
        state.CompilationFixAllowedFiles = WorkflowFindingRules.GetAllProposedFiles(state)
            .Select(f => f.RelativePath.Replace('\\', '/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        RestrictAllowedFilesForTestRecovery(state, timelineLabel, addTimeline);
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
        RestrictAllowedFilesForTestRecovery(state, timelineLabel, addTimeline);

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

    /// <summary>
    /// Test-focused / audit-only recovery: trim implementation paths from allowed files so recovery adds tests via prompt checklist + RAG.
    /// </summary>
    private static void RestrictAllowedFilesForTestRecovery(
        WorkflowState state,
        string timelineLabel,
        Action<string> addTimeline)
    {
        if (!ShouldRestrictAllowedFilesToBuildErrors(state))
        {
            return;
        }

        int before = state.CompilationFixAllowedFiles.Count;
        state.CompilationFixAllowedFiles = CollectCompilerReferencedPaths(state);
        string mode = MissingTestRecoverySupport.ShouldFocusOnMissingTests(state)
            ? "test-focused recovery"
            : "audit-only recovery";
        addTimeline(
            $"{timelineLabel}: {mode} — allowed files trimmed from {before} to "
            + $"{state.CompilationFixAllowedFiles.Count} (compiler-referenced only; add tests from checklist/RAG).");
    }

    internal static bool ShouldRestrictAllowedFilesToBuildErrors(WorkflowState state) =>
        state.Stage == WorkflowStage.Recovering
        && AuditorAgent.HasBlockingFindings(state.Audit)
        && (
            !WorkflowFindingRules.HasActionableBuildFindings(state)
            || MissingTestRecoverySupport.ShouldFocusOnMissingTests(state));

    internal static List<string> CollectCompilerReferencedPaths(WorkflowState state)
    {
        IReadOnlyList<AgentFinding> findings = state.BuildValidation?.Findings is { Count: > 0 } buildFindings
            ? buildFindings
            : Array.Empty<AgentFinding>();
        var paths = BuildFailureClassifier.CollectSourcePathsFromFindings(findings, state.RepoPath);
        foreach (string issue in state.ComplianceIssues)
        {
            foreach (string path in BuildFailureClassifier.CollectSourcePathsFromMessage(issue, state.RepoPath))
            {
                paths.Add(path.Replace('\\', '/').TrimStart('/'));
            }
        }

        return paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
