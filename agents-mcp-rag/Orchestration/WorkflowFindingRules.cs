using System.Text.RegularExpressions;
using agents_mcp_rag.Infrastructure;

static class WorkflowFindingRules
{
    private static readonly Regex FencedCodeBlockRegex = new(
        @"```[\s\S]*?```",
        RegexOptions.Compiled | RegexOptions.Multiline);

    public static List<GeneratedFile> GetAllProposedFiles(WorkflowState state)
    {
        return (state.Backend?.ProposedFiles ?? new List<GeneratedFile>())
            .Concat(state.Frontend?.ProposedFiles ?? new List<GeneratedFile>())
            .Concat(state.Recovery?.ProposedFiles ?? new List<GeneratedFile>())
            .ToList();
    }

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

    public static string FormatApplyRejectionComplianceIssue(string relativePath, string reason) =>
        $"Apply rejected '{relativePath}': {reason}";

    public static bool IsAgentFallback(AgentResult? result) =>
        result?.Summary.Contains("Fallback output because LLM call failed", StringComparison.OrdinalIgnoreCase) == true;

    public static AgentResult SanitizeArchitectureResult(AgentResult result) =>
        new()
        {
            AgentName = result.AgentName,
            Summary = StripArchitectureCodeBlocks(result.Summary),
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

    public static (bool HasBackend, bool HasFrontend) DetectRepoCapabilities(WorkflowState state)
    {
        RepoContract? contract = state.Contract;
        if (contract is not null)
        {
            bool hasFrontend = contract.Frontend is not null;
            bool hasBackend = contract.LayerConventions.GetActiveProfiles().Any();
            return (hasBackend, hasFrontend);
        }

        string structureContext = $"{state.ProjectStructureContext}\n{state.CombinedRagContext}";
        bool hasFrontendFromRag = structureContext.Contains("Frontend host:", StringComparison.OrdinalIgnoreCase)
                                  || structureContext.Contains("Feature modules root:", StringComparison.OrdinalIgnoreCase);
        bool hasBackendFromRag = structureContext.Contains("Layer '", StringComparison.OrdinalIgnoreCase)
                                 || structureContext.Contains("Entities:", StringComparison.OrdinalIgnoreCase)
                                 || structureContext.Contains("Repository interfaces namespace:", StringComparison.OrdinalIgnoreCase);

        return (hasBackendFromRag, hasFrontendFromRag);
    }

    public static string FormatRepoCapabilities(WorkflowState state)
    {
        (bool hasBackend, bool hasFrontend) = DetectRepoCapabilities(state);
        return $"backend={(hasBackend ? "yes" : "no")}, frontend={(hasFrontend ? "yes" : "no")}";
    }

    public static (bool RunBackend, bool RunFrontend) ResolveImplementationScope(WorkflowState state)
    {
        (bool projectHasBackend, bool projectHasFrontend) = DetectRepoCapabilities(state);
        string? architectureSummary = state.Architecture?.Summary;
        bool needsBackend = HasArchitectureSection(architectureSummary, "BACKEND_FILES")
                            || ExtractBackendPaths(architectureSummary).Count > 0;
        bool needsFrontend = HasArchitectureSection(architectureSummary, "FRONTEND_FILES")
                             || ExtractFrontendPaths(architectureSummary).Count > 0;

        // Architecture must list deliverables; if it omitted both sections, run every layer the repo actually has.
        if (!needsBackend && !needsFrontend)
        {
            needsBackend = projectHasBackend;
            needsFrontend = projectHasFrontend;
        }

        return (projectHasBackend && needsBackend, projectHasFrontend && needsFrontend);
    }

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

    private static bool HasArchitectureSection(string? architectureSummary, string sectionName)
    {
        if (string.IsNullOrWhiteSpace(architectureSummary))
        {
            return false;
        }

        return Regex.IsMatch(
            architectureSummary,
            $@"(?:^|\n)\s*#*\s*{Regex.Escape(sectionName)}\s*:?",
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
        string listLinePattern = $@"^\s*-\s*[`""']?([A-Za-z0-9_./\\-]+(?:{extPattern}))[`""']?\s*:";

        foreach (string pattern in new[] { fileLabelPattern, backtickPattern, listLinePattern })
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
