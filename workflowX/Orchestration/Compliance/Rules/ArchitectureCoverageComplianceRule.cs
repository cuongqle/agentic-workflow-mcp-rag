using workflowX.Infrastructure;

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
            ArchitectureDeliverableMatcher.BuildBackendAllowedPaths(context.State),
            context.State.Backend?.ProposedFiles,
            "BackendDeveloperAgent",
            findings);

        ValidateArchitectureDeliverables(
            context,
            ArchitectureDeliverableMatcher.BuildFrontendAllowedPaths(context.State),
            context.State.Frontend?.ProposedFiles,
            "FrontendDeveloperAgent",
            findings);

        return findings;
    }

    private static void ValidateArchitectureDeliverables(
        ComplianceContext context,
        IReadOnlyList<string> allowedPaths,
        IReadOnlyList<GeneratedFile>? agentProposedFiles,
        string agentName,
        List<AgentFinding> findings)
    {
        if (allowedPaths.Count == 0 || agentProposedFiles is null || agentProposedFiles.Count == 0)
        {
            return;
        }

        foreach (GeneratedFile extra in agentProposedFiles)
        {
            string normalized = extra.RelativePath.Replace('\\', '/').TrimStart('/');
            if (ArchitectureDeliverableMatcher.IsAllowedDeliverable(
                    normalized,
                    allowedPaths,
                    context.State.Contract))
            {
                continue;
            }

            findings.Add(new AgentFinding
            {
                Severity = FindingSeverity.High,
                Message =
                    $"Unexpected deliverable '{normalized}' from {agentName} is not listed in the architecture plan."
            });
        }

        foreach (string requiredPath in allowedPaths)
        {
            string normalized = requiredPath.Replace('\\', '/');
            GeneratedFile? proposedFile = agentProposedFiles.FirstOrDefault(file =>
                ArchitectureDeliverableMatcher.PathsMatch(file.RelativePath, normalized));

            if (proposedFile is null)
            {
                string? onDiskPath = FindRepoFile(context.RepoPath, normalized);
                if (!string.IsNullOrWhiteSpace(onDiskPath))
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

        }
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
