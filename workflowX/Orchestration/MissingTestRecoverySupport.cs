using workflowX.Infrastructure;

/// <summary>
/// Prompt-first test recovery: surfaces planned/missing test paths for <see cref="RecoveryAgent"/>.
/// </summary>
internal static class MissingTestRecoverySupport
{
    public static bool ShouldFocusOnMissingTests(WorkflowState state)
    {
        if (state.Stage is not WorkflowStage.Recovering)
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

        return GetMissingPlannedTestPaths(state).Count > 0
               || GetAppliedProductionMissingTests(state).Count > 0;
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

            string baseName = Path.GetFileNameWithoutExtension(appliedPath);
            if (HasMatchingTestFileOnDisk(state.RepoPath, baseName))
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
                   Add missing *Tests.cs using the same file/class naming pattern as a same-layer *Tests.cs exemplar in RAG. Never I-prefixed test names.

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

    private static bool HasMatchingTestFileOnDisk(string repoPath, string productionBaseName)
    {
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

    private static string NormalizePath(string path) =>
        path.Replace('\\', '/').Trim().TrimStart('/');
}
