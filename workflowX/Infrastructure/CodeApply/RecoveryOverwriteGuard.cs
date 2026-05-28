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
        if (errorPaths.Contains(normalizedTarget))
        {
            return true;
        }

        reason =
            $"Recovery rejected overwrite of '{relativePath}': no compiler error was reported for this file. "
            + "Edit only files referenced in build findings.";
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

        return paths;
    }

    private static string NormalizePath(string path) =>
        path.Replace('\\', '/').TrimStart('/');
}
