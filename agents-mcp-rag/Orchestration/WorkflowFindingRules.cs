using System.Text.RegularExpressions;
using agents_mcp_rag.Infrastructure;

static class WorkflowFindingRules
{
    private static readonly Regex FencedCodeBlockRegex = new(
        @"```[\s\S]*?```",
        RegexOptions.Compiled | RegexOptions.Multiline);

    public static List<GeneratedFile> GetAllProposedFiles(WorkflowState state) =>
        ProposedFileSupport.GetAllProposedFiles(state);

    public static bool HasBlockingFindings(IEnumerable<AgentFinding> findings)
    {
        return findings.Any(f => f.Severity is FindingSeverity.High or FindingSeverity.Blocker);
    }

    public static bool IsApplyRejectionComplianceIssue(string issue) =>
        issue.Contains("Apply rejected '", StringComparison.OrdinalIgnoreCase)
        || issue.Contains("Rejected recovery file '", StringComparison.OrdinalIgnoreCase)
        || issue.Contains("Rejected generated file '", StringComparison.OrdinalIgnoreCase)
        || issue.Contains("Compilation fix rejected '", StringComparison.OrdinalIgnoreCase);

    public static bool HasUnresolvedApplyRejections(WorkflowState state) =>
        state.ComplianceIssues.Any(IsApplyRejectionComplianceIssue);

    public static bool HasUnresolvedCompilationProblems(WorkflowState state) =>
        HasActionableBuildFindings(state) || HasUnresolvedApplyRejections(state);

    public static bool HasActionableBuildFindings(WorkflowState state)
    {
        if (state.BuildValidation?.Findings is not { Count: > 0 } findings)
        {
            return false;
        }

        RepoStack stack = RepoStack.From(state);
        return stack.DotNet
            ? findings.Any(BuildFailureClassifier.IsActionableFinding)
            : findings.Any(IsGenericActionableBuildFinding);
    }

    private static bool IsGenericActionableBuildFinding(AgentFinding finding) =>
        finding.Severity is FindingSeverity.High or FindingSeverity.Blocker
        && !string.IsNullOrWhiteSpace(finding.Message);

    public static string FormatApplyRejectionComplianceIssue(string relativePath, string reason) =>
        $"Apply rejected '{relativePath}': {reason}";

    public static bool IsAgentFallback(AgentResult? result) =>
        result?.Summary.Contains("Fallback output because LLM call failed", StringComparison.OrdinalIgnoreCase) == true;

    public static AgentResult SanitizeArchitectureResult(AgentResult result) =>
        new()
        {
            AgentName = result.AgentName,
            Summary = StripArchitectureCodeBlocks(result.Summary),
            ArchitecturePlan = result.ArchitecturePlan,
            ProposedFiles = result.ProposedFiles,
            Findings = result.Findings,
            ProductionBuildPassed = result.ProductionBuildPassed,
            TestsPassed = result.TestsPassed
        };

    public static string StripArchitectureCodeBlocks(string? summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            return string.Empty;
        }

        string withoutCode = FencedCodeBlockRegex.Replace(summary, string.Empty);
        return Regex.Replace(withoutCode, @"\n{3,}", "\n\n").Trim();
    }

    public static RepoStack DetectRepoStack(WorkflowState state) =>
        RepoStack.From(state);

    public static (bool HasBackend, bool HasFrontend) DetectRepoCapabilities(WorkflowState state)
    {
        RepoStack stack = DetectRepoStack(state);
        return (stack.DotNet, stack.Frontend);
    }

    public static string FormatRepoCapabilities(WorkflowState state)
    {
        (bool hasBackend, bool hasFrontend) = DetectRepoCapabilities(state);
        return $"backend={(hasBackend ? "yes" : "no")}, frontend={(hasFrontend ? "yes" : "no")}";
    }

    public static IReadOnlyList<string> GetBackendPaths(WorkflowState state) =>
        state.ArchitecturePlan?.BackendPaths.Count > 0
            ? state.ArchitecturePlan.BackendPaths
            : ExtractBackendPaths(state.Architecture?.Summary);

    public static IReadOnlyList<string> GetFrontendPaths(WorkflowState state) =>
        state.ArchitecturePlan?.FrontendPaths.Count > 0
            ? state.ArchitecturePlan.FrontendPaths
            : ExtractFrontendPaths(state.Architecture?.Summary);

    public static (bool RunBackend, bool RunFrontend) ResolveImplementationScope(WorkflowState state) =>
        ResolveImplementationScopeDetails(state).Scope;

    public static ImplementationScopeDetails ResolveImplementationScopeDetails(WorkflowState state)
    {
        (bool projectHasBackend, bool projectHasFrontend) = DetectRepoCapabilities(state);
        ArchitecturePlan? plan = state.ArchitecturePlan;
        string? architectureSummary = state.Architecture?.Summary;

        bool needsBackend = plan?.HasBackendDeliverables == true
                            || HasArchitectureSection(architectureSummary, "BACKEND_FILES")
                            || ExtractBackendPaths(architectureSummary).Count > 0;
        bool needsFrontend = plan?.HasFrontendDeliverables == true
                             || HasArchitectureSection(architectureSummary, "FRONTEND_FILES")
                             || ExtractFrontendPaths(architectureSummary).Count > 0;

        string planSource = plan?.HasBackendDeliverables == true || plan?.HasFrontendDeliverables == true
            ? "structured-plan"
            : needsBackend || needsFrontend
                ? "markdown-parsed"
                : "none";

        if (!needsBackend && !needsFrontend)
        {
            needsBackend = projectHasBackend;
            needsFrontend = projectHasFrontend;
            if (projectHasBackend || projectHasFrontend)
            {
                planSource = "repo-fallback";
            }
        }

        return new ImplementationScopeDetails(
            Scope: (projectHasBackend && needsBackend, projectHasFrontend && needsFrontend),
            ProjectHasBackend: projectHasBackend,
            ProjectHasFrontend: projectHasFrontend,
            NeedsBackend: needsBackend,
            NeedsFrontend: needsFrontend,
            BackendPathCount: GetBackendPaths(state).Count,
            FrontendPathCount: GetFrontendPaths(state).Count,
            PlanSource: planSource);
    }

    public static string FormatImplementationScopeDiagnostics(ImplementationScopeDetails details) =>
        $"Implementation scope: backend={details.Scope.RunBackend}, frontend={details.Scope.RunFrontend} "
        + $"(repo backend={(details.ProjectHasBackend ? "yes" : "no")}, frontend={(details.ProjectHasFrontend ? "yes" : "no")}; "
        + $"needsBackend={details.NeedsBackend}, needsFrontend={details.NeedsFrontend}; "
        + $"deliverables backend={details.BackendPathCount}, frontend={details.FrontendPathCount}; "
        + $"source={details.PlanSource}).";

    public static int CountProposedImplementationFiles(WorkflowState state) =>
        (state.Backend?.ProposedFiles.Count ?? 0) + (state.Frontend?.ProposedFiles.Count ?? 0);

    public static string DescribeMissingImplementationReason(bool runBackend, bool runFrontend, WorkflowState state)
    {
        if (!runBackend && !runFrontend)
        {
            (bool hasBackend, bool hasFrontend) = DetectRepoCapabilities(state);
            return $"No implementation agents ran (repo backend={hasBackend}, frontend={hasFrontend}). "
                   + "Architecture plan must include BACKEND_FILES and/or FRONTEND_FILES with file paths.";
        }

        if (runBackend && (state.Backend?.ProposedFiles.Count ?? 0) == 0)
        {
            return "BackendDeveloperAgent ran but returned no files in JSON output.";
        }

        if (runFrontend && (state.Frontend?.ProposedFiles.Count ?? 0) == 0)
        {
            return "FrontendDeveloperAgent ran but returned no files in JSON output.";
        }

        return "Developer agents produced no applicable files.";
    }

    public static AgentResult SkippedAgentResult(string agentName, string summary) =>
        new()
        {
            AgentName = agentName,
            Summary = summary
        };

    public static IReadOnlyList<string> ExtractBackendPaths(string? architectureSummary) =>
        ExtractArchitecturePaths(architectureSummary, ".cs");

    public static IReadOnlyList<string> ExtractFrontendPaths(string? architectureSummary) =>
        ExtractArchitecturePaths(architectureSummary, ".js", ".html", ".ts", ".vue", ".cshtml");

    internal static bool HasArchitectureSection(string? architectureSummary, string sectionName)
    {
        if (string.IsNullOrWhiteSpace(architectureSummary))
        {
            return false;
        }

        string escaped = Regex.Escape(sectionName);
        return Regex.IsMatch(
            architectureSummary,
            $@"(?:^|\n)\s*(?:#{{1,3}}\s*|\*{{1,2}})?{escaped}(?:\*{{1,2}})?\s*:?",
            RegexOptions.IgnoreCase);
    }

    public static IReadOnlyList<string> ExtractArchitecturePaths(string? architectureSummary, params string[] extensions)
    {
        if (string.IsNullOrWhiteSpace(architectureSummary) || extensions.Length == 0)
        {
            return Array.Empty<string>();
        }

        string extPattern = string.Join("|", extensions.Select(e => Regex.Escape(e.StartsWith('.') ? e : "." + e)));
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string fileLabelPattern = $@"\bFile:\s*[`""']?([^\s`""']+(?:{extPattern}))[`""']?";
        string backtickPattern = $@"`([A-Za-z0-9_./\\-]+(?:{extPattern}))`";
        string bulletListPattern = $@"^\s*-\s*[`""']?([A-Za-z0-9_./\\-]+(?:{extPattern}))[`""']?\s*:";
        string numberedListPattern = $@"^\s*\d+\.\s*[`""']?([A-Za-z0-9_./\\-]+(?:{extPattern}))[`""']?\s*:";

        foreach (string pattern in new[] { fileLabelPattern, backtickPattern, bulletListPattern, numberedListPattern })
        {
            foreach (Match match in Regex.Matches(architectureSummary, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline))
            {
                AddArchitecturePath(paths, match.Groups[1].Value, extensions);
            }
        }

        return paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void AddArchitecturePath(HashSet<string> paths, string raw, string[] extensions)
    {
        string normalized = raw.Trim().Replace('\\', '/').Trim('`', '"', '\'');
        if (extensions.Any(ext => normalized.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
        {
            paths.Add(normalized);
        }
    }
}

readonly record struct ImplementationScopeDetails(
    (bool RunBackend, bool RunFrontend) Scope,
    bool ProjectHasBackend,
    bool ProjectHasFrontend,
    bool NeedsBackend,
    bool NeedsFrontend,
    int BackendPathCount,
    int FrontendPathCount,
    string PlanSource);
