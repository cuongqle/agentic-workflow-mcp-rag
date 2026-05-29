using workflowX.Infrastructure.Compliance.DotNet;

namespace workflowX.Infrastructure;

/// <summary>
/// During recovery, blocks overwriting existing on-disk files that are not named in compiler output.
/// </summary>
internal static class RecoveryOverwriteGuard
{
    internal static bool TryValidateOverwrite(
        WorkflowState state,
        string repoPath,
        string relativePath,
        bool existedBefore,
        out string reason)
    {
        reason = string.Empty;
        if (state.Stage is not (WorkflowStage.Recovering or WorkflowStage.Integrating))
        {
            return true;
        }

        if (!existedBefore)
        {
            return true;
        }

        string normalizedTarget = NormalizePath(relativePath);
        HashSet<string> errorPaths = CollectCompilerErrorPaths(state, repoPath);
        if (IsAllowedRecoveryOverwrite(repoPath, normalizedTarget, errorPaths))
        {
            return true;
        }

        reason =
            $"Recovery rejected overwrite of '{relativePath}': no compiler error was reported for this file. "
            + "Edit only files referenced in build findings (use exact repo-relative paths — do not duplicate the repository folder name).";
        return false;
    }

    internal static bool IsAllowedRecoveryOverwrite(
        string repoPath,
        string normalizedTarget,
        HashSet<string> errorPaths)
    {
        if (errorPaths.Contains(normalizedTarget))
        {
            return true;
        }

        foreach (string errorPath in errorPaths)
        {
            if (ArchitectureDeliverableMatcher.PathsMatch(normalizedTarget, errorPath))
            {
                return true;
            }
        }

        if (!normalizedTarget.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (string errorPath in errorPaths)
        {
            if (!errorPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? owningTestCsproj = TestProjectPathSupport.TryResolveOwningTestCsproj(repoPath, errorPath);
            if (string.IsNullOrWhiteSpace(owningTestCsproj))
            {
                continue;
            }

            if (ArchitectureDeliverableMatcher.PathsMatch(normalizedTarget, owningTestCsproj))
            {
                return true;
            }
        }

        return false;
    }

    private static HashSet<string> CollectCompilerErrorPaths(WorkflowState state, string repoPath)
    {
        IReadOnlyList<AgentFinding> findings = state.BuildValidation?.Findings is { Count: > 0 } buildFindings
            ? buildFindings
            : Array.Empty<AgentFinding>();
        var paths = BuildFailureClassifier.CollectSourcePathsFromFindings(findings, repoPath);

        foreach (string issue in state.ComplianceIssues)
        {
            foreach (string path in BuildFailureClassifier.CollectSourcePathsFromMessage(issue, repoPath))
            {
                paths.Add(NormalizePath(path));
            }
        }

        TestProjectPathSupport.ExpandWithOwningTestProjects(repoPath, paths);
        BuildFailureClassifier.ExpandDuplicateAssemblyAttributeSources(repoPath, findings, paths);
        return paths;
    }

    private static string NormalizePath(string path) =>
        path.Replace('\\', '/').TrimStart('/');
}
