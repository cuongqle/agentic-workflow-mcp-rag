using workflowX.Infrastructure.CodeApply.DotNet;
using workflowX.Infrastructure.Compliance.DotNet;

namespace workflowX.Infrastructure;

/// <summary>
/// Bridges workflow orchestration to .NET-specific recovery apply rules.
/// </summary>
internal static class RecoveryApplySupport
{
    internal static bool TryValidateDotNetRecoveryOverwrite(
        WorkflowState state,
        RepoStack stack,
        string repoPath,
        string relativePath,
        bool existedBefore,
        out string reason)
    {
        reason = string.Empty;
        if (!stack.DotNet)
        {
            return true;
        }

        if (state.Stage is not (WorkflowStage.Recovering or WorkflowStage.Integrating))
        {
            return true;
        }

        HashSet<string> allowedPaths = BuildAllowedRecoveryOverwritePaths(state, repoPath);
        string canonicalPath = RecoveryPathSupport.CanonicalizeRecoveryPath(relativePath);
        return DotNetRecoveryOverwriteGuard.TryValidateOverwrite(
            repoPath,
            canonicalPath,
            existedBefore,
            allowedPaths,
            out reason);
    }

    internal static HashSet<string> BuildAllowedRecoveryOverwritePaths(WorkflowState state, string repoPath)
    {
        HashSet<string> paths = BuildCompilerReferencedRecoveryPaths(state, repoPath);
        IncludeRecoveryAgentOutputPaths(state, paths);
        return paths;
    }

    /// <summary>
    /// Compiler output, compliance, and discovered test targets — used to trim recovery allowed-file list.
    /// </summary>
    internal static HashSet<string> BuildCompilerReferencedRecoveryPaths(WorkflowState state, string repoPath)
    {
        IReadOnlyList<AgentFinding> findings = state.BuildValidation?.Findings is { Count: > 0 } buildFindings
            ? buildFindings
            : Array.Empty<AgentFinding>();
        var paths = BuildFailureClassifier.CollectSourcePathsFromFindings(findings, repoPath);

        foreach (string issue in state.ComplianceIssues)
        {
            foreach (string path in BuildFailureClassifier.CollectSourcePathsFromMessage(issue, repoPath))
            {
                paths.Add(RecoveryPathSupport.CanonicalizeRecoveryPath(path));
            }
        }

        if (MissingTestRecoverySupport.ShouldAllowTestRecoveryOverwrites(state))
        {
            foreach (string testPath in MissingTestRecoverySupport.CollectRecoveryTestOverwritePaths(state, repoPath))
            {
                paths.Add(RecoveryPathSupport.CanonicalizeRecoveryPath(testPath));
            }
        }

        TestProjectPathSupport.ExpandWithOwningProjects(repoPath, paths);
        if (MissingTestRecoverySupport.ShouldAllowTestRecoveryOverwrites(state))
        {
            TestProjectPathSupport.ExpandWithOwningTestProjects(repoPath, paths);
        }

        BuildFailureClassifier.ExpandDuplicateAssemblyAttributeSources(repoPath, findings, paths);
        return paths;
    }

    private static void IncludeRecoveryAgentOutputPaths(WorkflowState state, HashSet<string> paths)
    {
        foreach (string path in state.CompilationFixAllowedFiles)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                paths.Add(RecoveryPathSupport.CanonicalizeRecoveryPath(path));
            }
        }

        foreach (GeneratedFile file in state.Recovery?.ProposedFiles ?? Enumerable.Empty<GeneratedFile>())
        {
            if (!string.IsNullOrWhiteSpace(file.RelativePath))
            {
                paths.Add(RecoveryPathSupport.CanonicalizeRecoveryPath(file.RelativePath));
            }
        }
    }
}
