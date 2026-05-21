using System.Text.RegularExpressions;

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

    public static IReadOnlyList<string> ExtractBackendPaths(string? architectureSummary) =>
        ExtractArchitecturePaths(architectureSummary, ".cs");

    public static IReadOnlyList<string> ExtractFrontendPaths(string? architectureSummary) =>
        ExtractArchitecturePaths(architectureSummary, ".js", ".html", ".ts", ".vue", ".cshtml");

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
