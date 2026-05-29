using System.Text.RegularExpressions;
using workflowX.Infrastructure;
using workflowX.Infrastructure.Compliance.DotNet;
using workflowX.Infrastructure.Exemplar.DotNet;

/// <summary>
/// Prompt-first test recovery: surfaces planned/missing test paths for <see cref="RecoveryAgent"/>.
/// </summary>
internal static class MissingTestRecoverySupport
{
    private static readonly Regex BareTestFileNameRegex = new(
        @"\b([A-Za-z][A-Za-z0-9_]*Tests\.cs)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool ShouldFocusOnMissingTests(WorkflowState state) =>
        ShouldAllowTestRecoveryOverwrites(state);

    /// <summary>
    /// Recovery apply may create/overwrite test files (Recovering or Integrating compilation-fix loop).
    /// </summary>
    public static bool ShouldAllowTestRecoveryOverwrites(WorkflowState state)
    {
        if (state.Stage is not (WorkflowStage.Recovering or WorkflowStage.Integrating))
        {
            return false;
        }

        if (state.BuildValidation?.TestsPassed == false)
        {
            return true;
        }

        if (HasMissingTestAuditSignal(state))
        {
            return true;
        }

        if (GetMissingPlannedTestPaths(state).Count > 0
            || GetAppliedProductionMissingTests(state).Count > 0)
        {
            return true;
        }

        return WorkflowFindingRules.HasUnresolvedApplyRejections(state)
               && HasTestRelatedApplyRejection(state);
    }

    public static bool HasTestRelatedApplyRejection(WorkflowState state) =>
        state.ComplianceIssues.Any(issue =>
            issue.Contains("Tests.cs", StringComparison.OrdinalIgnoreCase)
            || issue.Contains(".spec.", StringComparison.OrdinalIgnoreCase)
            || issue.Contains("UnitTest", StringComparison.OrdinalIgnoreCase)
            || issue.Contains(".Tests/", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// All test paths recovery may create or overwrite (plan, exemplar discovery, audit, prior apply rejections).
    /// </summary>
    public static IReadOnlyList<string> CollectRecoveryTestOverwritePaths(WorkflowState state, string repoPath)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in GetPlannedTestPaths(state))
        {
            paths.Add(path);
        }

        foreach (string path in GetMissingPlannedTestPaths(state))
        {
            paths.Add(path);
        }

        if (!string.IsNullOrWhiteSpace(repoPath) && Directory.Exists(repoPath))
        {
            IReadOnlyList<string> productionPlanned = WorkflowFindingRules.GetBackendPaths(state)
                .Where(path => !IsTestArtifactPath(path))
                .ToList();
            foreach (string discovered in ExemplarTestCompanionSupport.DiscoverMissingTestPaths(repoPath, productionPlanned))
            {
                paths.Add(NormalizePath(discovered));
            }

            foreach (string productionPath in GetAppliedProductionMissingTests(state))
            {
                string? expectedTestPath = ExemplarTestCompanionSupport.DiscoverExpectedTestPath(repoPath, productionPath);
                if (!string.IsNullOrWhiteSpace(expectedTestPath))
                {
                    paths.Add(NormalizePath(expectedTestPath));
                }
            }

            foreach (string appliedPath in state.AppliedFiles)
            {
                if (!IsProductionSourcePath(appliedPath))
                {
                    continue;
                }

                string? expectedTestPath = ExemplarTestCompanionSupport.DiscoverExpectedTestPath(repoPath, appliedPath);
                if (!string.IsNullOrWhiteSpace(expectedTestPath))
                {
                    paths.Add(NormalizePath(expectedTestPath));
                }
            }
        }

        CollectAuditTestPaths(state, repoPath, paths);
        CollectComplianceTestPaths(state, repoPath, paths);
        return paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void CollectAuditTestPaths(WorkflowState state, string repoPath, HashSet<string> paths)
    {
        if (state.Audit?.Findings is null)
        {
            return;
        }

        foreach (AgentFinding finding in state.Audit.Findings
                     .Where(finding => finding.Severity is FindingSeverity.High or FindingSeverity.Blocker))
        {
            foreach (string path in BuildFailureClassifier.CollectSourcePathsFromMessage(finding.Message, repoPath))
            {
                if (IsTestArtifactPath(path) && HasRepoRelativeDirectory(path))
                {
                    paths.Add(NormalizePath(path));
                }
            }

            foreach (Match match in BareTestFileNameRegex.Matches(finding.Message))
            {
                string? resolved = TryResolveBareTestFileName(repoPath, match.Groups[1].Value);
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    paths.Add(resolved);
                }
            }
        }
    }

    private static void CollectComplianceTestPaths(WorkflowState state, string repoPath, HashSet<string> paths)
    {
        foreach (string issue in state.ComplianceIssues)
        {
            foreach (string path in BuildFailureClassifier.CollectSourcePathsFromMessage(issue, repoPath))
            {
                if (IsTestArtifactPath(path) && HasRepoRelativeDirectory(path))
                {
                    paths.Add(NormalizePath(path));
                }
            }

            foreach (Match match in BareTestFileNameRegex.Matches(issue))
            {
                string? resolved = TryResolveBareTestFileName(repoPath, match.Groups[1].Value);
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    paths.Add(resolved);
                }
            }
        }
    }

    private static string? TryResolveBareTestFileName(string repoPath, string fileName)
    {
        if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
        {
            return null;
        }

        foreach (string absolute in Directory.EnumerateFiles(repoPath, fileName, SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(repoPath, absolute).Replace('\\', '/');
            if (relative.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                || relative.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return NormalizePath(relative);
        }

        if (!fileName.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string productionFileName = fileName[..^"Tests.cs".Length] + ".cs";
        foreach (string absolute in Directory.EnumerateFiles(repoPath, productionFileName, SearchOption.AllDirectories))
        {
            string productionRelative = Path.GetRelativePath(repoPath, absolute).Replace('\\', '/');
            if (productionRelative.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                || productionRelative.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? expected = ExemplarTestCompanionSupport.DiscoverExpectedTestPath(repoPath, productionRelative);
            if (!string.IsNullOrWhiteSpace(expected))
            {
                return NormalizePath(expected);
            }
        }

        return null;
    }

    public static bool HasMissingTestAuditSignal(WorkflowState state)
    {
        if (state.Audit?.Findings is null)
        {
            return false;
        }

        return state.Audit.Findings
            .Where(finding => finding.Severity is FindingSeverity.High or FindingSeverity.Blocker)
            .Any(finding =>
            {
                string message = finding.Message;
                return message.Contains("missing", StringComparison.OrdinalIgnoreCase)
                       && message.Contains("test", StringComparison.OrdinalIgnoreCase)
                       || message.Contains("Tests.cs", StringComparison.OrdinalIgnoreCase)
                       || message.Contains("automated tests failed", StringComparison.OrdinalIgnoreCase);
            });
    }

    public static IReadOnlyList<string> GetPlannedTestPaths(WorkflowState state)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in WorkflowFindingRules.GetBackendPaths(state))
        {
            if (IsTestArtifactPath(path))
            {
                paths.Add(NormalizePath(path));
            }
        }

        foreach (string path in WorkflowFindingRules.GetFrontendPaths(state))
        {
            if (IsTestArtifactPath(path))
            {
                paths.Add(NormalizePath(path));
            }
        }

        return paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static IReadOnlyList<string> GetMissingPlannedTestPaths(WorkflowState state)
    {
        if (string.IsNullOrWhiteSpace(state.RepoPath))
        {
            return GetPlannedTestPaths(state);
        }

        return GetPlannedTestPaths(state)
            .Where(path => !File.Exists(Path.Combine(state.RepoPath, path.Replace('/', Path.DirectorySeparatorChar))))
            .ToList();
    }

    public static IReadOnlyList<string> GetAppliedProductionMissingTests(WorkflowState state)
    {
        if (string.IsNullOrWhiteSpace(state.RepoPath) || !Directory.Exists(state.RepoPath))
        {
            return Array.Empty<string>();
        }

        var missing = new List<string>();
        foreach (string appliedPath in state.AppliedFiles)
        {
            if (!IsProductionSourcePath(appliedPath))
            {
                continue;
            }

            if (HasMatchingTestFileOnDisk(state.RepoPath, appliedPath))
            {
                continue;
            }

            missing.Add(NormalizePath(appliedPath));
        }

        return missing;
    }

    public static string BuildPromptSection(WorkflowState state)
    {
        if (!ShouldFocusOnMissingTests(state))
        {
            return string.Empty;
        }

        IReadOnlyList<string> plannedMissing = GetMissingPlannedTestPaths(state);
        IReadOnlyList<string> plannedAll = GetPlannedTestPaths(state);
        IReadOnlyList<string> productionMissing = GetAppliedProductionMissingTests(state);
        if (plannedMissing.Count == 0 && plannedAll.Count == 0 && productionMissing.Count == 0)
        {
            return """

                   Test recovery focus: automated tests failed or audit flagged missing tests.
                   Add missing tests using the same file/class naming pattern as a same-kind test exemplar in RAG. Never I-prefixed test names.

                   """;
        }

        var lines = new List<string>
        {
            "Test recovery focus (address before other edits):",
            "- Create every checklist path below (mirror RAG *Tests.cs exemplar path and naming; test class name matches file name, same pattern as exemplar).",
            "- Do not return production files from Files already applied unless Build errors name that exact path."
        };

        if (plannedMissing.Count > 0)
        {
            lines.Add("- Architecture planned test files still missing on disk:");
            lines.AddRange(plannedMissing.Select(path => $"  - {path}"));
        }
        else if (plannedAll.Count > 0)
        {
            lines.Add("- Architecture planned test files (verify on disk; re-create if empty or failing):");
            lines.AddRange(plannedAll.Select(path => $"  - {path}"));
        }

        if (productionMissing.Count > 0)
        {
            lines.Add("- Applied production files without a matching *Tests.cs on disk (add tests using RAG exemplar folders):");
            lines.AddRange(productionMissing.Select(path => $"  - {path}"));
        }

        if (state.BuildValidation?.TestsPassed == false)
        {
            lines.Add("- dotnet test failed: after adding/fixing tests above, ensure new tests compile and assertions match production contracts.");
        }

        return "\n" + string.Join('\n', lines) + "\n";
    }

    private static bool HasMatchingTestFileOnDisk(string repoPath, string productionRelativePath)
    {
        string? expectedPath = ExemplarTestCompanionSupport.DiscoverExpectedTestPath(repoPath, productionRelativePath);
        if (!string.IsNullOrWhiteSpace(expectedPath))
        {
            string absolute = Path.Combine(repoPath, expectedPath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(absolute))
            {
                return true;
            }
        }

        string productionBaseName = Path.GetFileNameWithoutExtension(
            Path.GetFileName(productionRelativePath.Replace('\\', '/')));
        string expectedName = productionBaseName + "Tests.cs";
        foreach (string absolute in Directory.EnumerateFiles(repoPath, expectedName, SearchOption.AllDirectories))
        {
            string normalized = absolute.Replace('\\', '/');
            if (!normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                && !normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTestArtifactPath(string path)
    {
        string normalized = NormalizePath(path);
        return normalized.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains(".spec.", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains(".test.", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsProductionSourcePath(string path)
    {
        string normalized = NormalizePath(path);
        if (IsTestArtifactPath(normalized))
        {
            return false;
        }

        return normalized.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasRepoRelativeDirectory(string path) =>
        NormalizePath(path).Contains('/', StringComparison.Ordinal);

    private static string NormalizePath(string path) =>
        path.Replace('\\', '/').Trim().TrimStart('/');
}
