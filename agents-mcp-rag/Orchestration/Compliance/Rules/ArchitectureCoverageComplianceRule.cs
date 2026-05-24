using agents_mcp_rag.Infrastructure;

sealed class ArchitectureCoverageComplianceRule : IComplianceRule
{
    public string RuleId => "architecture.deliverable-coverage";
    public string Category => "architecture";

    public bool AppliesTo(ComplianceContext context) =>
        !string.IsNullOrWhiteSpace(context.State.Architecture?.Summary)
        || context.State.ArchitecturePlan?.HasBackendDeliverables == true
        || context.State.ArchitecturePlan?.HasFrontendDeliverables == true;

    public IEnumerable<AgentFinding> Evaluate(ComplianceContext context)
    {
        var findings = new List<AgentFinding>();

        ValidateArchitectureDeliverables(
            context,
            WorkflowFindingRules.GetBackendPaths(context.State),
            context.State.Backend?.ProposedFiles,
            "BackendDeveloperAgent",
            findings);

        ValidateArchitectureDeliverables(
            context,
            WorkflowFindingRules.GetFrontendPaths(context.State),
            context.State.Frontend?.ProposedFiles,
            "FrontendDeveloperAgent",
            findings);

        return findings;
    }

    private static void ValidateArchitectureDeliverables(
        ComplianceContext context,
        IReadOnlyList<string> requiredPaths,
        IReadOnlyList<GeneratedFile>? proposedFiles,
        string agentName,
        List<AgentFinding> findings)
    {
        if (requiredPaths.Count == 0)
        {
            return;
        }

        proposedFiles ??= new List<GeneratedFile>();
        foreach (string requiredPath in requiredPaths)
        {
            string normalized = requiredPath.Replace('\\', '/');
            GeneratedFile? proposedFile = proposedFiles.FirstOrDefault(file =>
                PathsMatch(file.RelativePath, normalized));

            if (proposedFile is null)
            {
                string? onDiskPath = FindRepoFile(context.RepoPath, normalized);
                if (!string.IsNullOrWhiteSpace(onDiskPath)
                    && !PlaceholderImplementationGuard.ContainsPlaceholderMarkers(File.ReadAllText(onDiskPath)))
                {
                    continue;
                }

                findings.Add(new AgentFinding
                {
                    Severity = FindingSeverity.High,
                    Message =
                        $"Architecture requires '{normalized}' but {agentName} did not include it in proposed files."
                });
                continue;
            }

            if (PlaceholderImplementationGuard.ContainsPlaceholderMarkers(proposedFile.Content))
            {
                findings.Add(new AgentFinding
                {
                    Severity = FindingSeverity.High,
                    Message =
                        $"Architecture deliverable '{normalized}' from {agentName} contains placeholder/stub content."
                });
            }
        }
    }

    private static bool PathsMatch(string proposedRelativePath, string requiredPath)
    {
        string proposed = proposedRelativePath.Replace('\\', '/');
        return proposed.Equals(requiredPath, StringComparison.OrdinalIgnoreCase)
               || proposed.EndsWith("/" + requiredPath, StringComparison.OrdinalIgnoreCase)
               || proposed.EndsWith("/" + Path.GetFileName(requiredPath), StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindRepoFile(string repoPath, string relativePath)
    {
        string direct = Path.Combine(repoPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(direct))
        {
            return direct;
        }

        string fileName = Path.GetFileName(relativePath);
        return Directory
            .EnumerateFiles(repoPath, fileName, SearchOption.AllDirectories)
            .FirstOrDefault(path => !path.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
                                 && !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                                 && !path.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase)
                                 && !path.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase));
    }
}
