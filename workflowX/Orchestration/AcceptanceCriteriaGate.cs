using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using workflowX.Configuration;

static class AcceptanceCriteriaReportParser
{
    public static bool TryParseJson(string raw, RequirementsSpec requirements, out AcceptanceCriteriaReport? report, out string summary)
    {
        report = null;
        summary = raw;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        string candidate = ExtractJsonCandidate(raw);
        try
        {
            using var document = JsonDocument.Parse(candidate);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            summary = ReadStringProperty(root, "summary");
            var evaluations = new List<AcceptanceCriterionEvaluation>();
            if (root.TryGetProperty("criteriaResults", out JsonElement resultsNode) && resultsNode.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in resultsNode.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    string id = item.TryGetProperty("id", out JsonElement idNode) ? idNode.GetString()?.Trim() ?? string.Empty : string.Empty;
                    bool passed = item.TryGetProperty("passed", out JsonElement passedNode)
                                  && passedNode.ValueKind is JsonValueKind.True or JsonValueKind.False
                                  && passedNode.GetBoolean();
                    string evidence = item.TryGetProperty("evidence", out JsonElement evidenceNode)
                        ? evidenceNode.GetString()?.Trim() ?? string.Empty
                        : string.Empty;
                    string description = ResolveDescription(requirements, id);
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        continue;
                    }

                    evaluations.Add(new AcceptanceCriterionEvaluation(id, description, passed, evidence, "llm"));
                }
            }

            report = new AcceptanceCriteriaReport { Evaluations = evaluations };
            summary = string.IsNullOrWhiteSpace(summary) ? FormatReadableSummary(report) : summary;
            return evaluations.Count > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static string FormatReadableSummary(AcceptanceCriteriaReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Acceptance criteria: {report.PassedCount}/{report.Evaluations.Count} passed.");
        sb.AppendLine();
        foreach (AcceptanceCriterionEvaluation evaluation in report.Evaluations)
        {
            sb.Append("- ");
            sb.Append(evaluation.Id);
            sb.Append(": ");
            sb.Append(evaluation.Passed ? "PASS" : "FAIL");
            if (!string.IsNullOrWhiteSpace(evaluation.Description))
            {
                sb.Append(" — ");
                sb.Append(evaluation.Description);
            }

            if (!string.IsNullOrWhiteSpace(evaluation.Evidence))
            {
                sb.Append(" (");
                sb.Append(evaluation.Evidence);
                sb.Append(')');
            }

            sb.AppendLine();
        }

        return sb.ToString().Trim();
    }

    private static string ResolveDescription(RequirementsSpec requirements, string id) =>
        requirements.AcceptanceCriteria.FirstOrDefault(criterion =>
            criterion.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.Description ?? string.Empty;

    private static string ReadStringProperty(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out JsonElement node) && node.ValueKind == JsonValueKind.String
            ? node.GetString() ?? string.Empty
            : string.Empty;

    private static string ExtractJsonCandidate(string raw)
    {
        var fenced = Regex.Match(raw, "```(?:json)?\\s*(\\{[\\s\\S]*\\})\\s*```", RegexOptions.IgnoreCase);
        if (fenced.Success && fenced.Groups.Count > 1)
        {
            return fenced.Groups[1].Value;
        }

        int firstBrace = raw.IndexOf('{');
        int lastBrace = raw.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            return raw.Substring(firstBrace, lastBrace - firstBrace + 1);
        }

        return raw;
    }
}

static class AcceptanceCriteriaGate
{
    private static readonly string[] BuildKeywords = ["build", "compile", "compiles", "compilation"];
    private static readonly string[] TestKeywords = ["test", "unit test", "tests pass", "passing test"];

    public static AcceptanceCriteriaReport EvaluateDeterministic(WorkflowState state, AcceptanceCriteriaOptions options)
    {
        RequirementsSpec requirements = state.RequirementsSpec ?? new RequirementsSpec();
        var evaluations = new List<AcceptanceCriterionEvaluation>();

        if (options.Enabled && !requirements.HasAcceptanceCriteria)
        {
            evaluations.Add(new AcceptanceCriterionEvaluation(
                "GATE",
                "Requirements must define at least one acceptance criterion.",
                false,
                "No acceptance criteria were parsed from requirements output.",
                "deterministic"));
            return new AcceptanceCriteriaReport { Evaluations = evaluations };
        }

        if (options.Enabled && requirements.AcceptanceCriteria.Count < options.MinimumCriteriaCount)
        {
            evaluations.Add(new AcceptanceCriterionEvaluation(
                "GATE",
                $"At least {options.MinimumCriteriaCount} acceptance criterion/criteria required.",
                false,
                $"Parsed {requirements.AcceptanceCriteria.Count} criterion/criteria.",
                "deterministic"));
        }

        if (options.RequireProductionBuildPass && state.BuildValidation?.ProductionBuildPassed != true)
        {
            evaluations.Add(new AcceptanceCriterionEvaluation(
                "GATE-BUILD",
                "Production build must pass before release.",
                false,
                state.BuildValidation?.Summary ?? "Build validation did not report a passing production build.",
                "deterministic"));
        }

        HashSet<string> repoPaths = CollectRepoRelativePaths(state);
        var evaluatedCriteria = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in WorkflowFindingRules.GetBackendPaths(state).Concat(WorkflowFindingRules.GetFrontendPaths(state)))
        {
            bool exists = repoPaths.Contains(NormalizePath(path));
            evaluations.Add(new AcceptanceCriterionEvaluation(
                $"DELIVERABLE:{path}",
                $"Architecture deliverable exists on disk: {path}",
                exists,
                exists ? "File found in repository after apply." : "Expected file was not found in repository after apply.",
                "deterministic"));
        }

        foreach (AcceptanceCriterion criterion in requirements.AcceptanceCriteria)
        {
            if (evaluatedCriteria.Contains(criterion.Id))
            {
                continue;
            }

            bool requiresBuild = MatchesAnyKeyword(criterion.Description, BuildKeywords);
            bool requiresTests = MatchesAnyKeyword(criterion.Description, TestKeywords);
            if (!requiresBuild && !requiresTests)
            {
                continue;
            }

            bool passed = true;
            var evidenceParts = new List<string>();
            if (requiresBuild)
            {
                bool buildPassed = state.BuildValidation?.ProductionBuildPassed == true;
                passed &= buildPassed;
                evidenceParts.Add(buildPassed ? "Production build passed." : "Production build failed or was not verified.");
            }

            if (requiresTests)
            {
                bool? testsPassed = state.BuildValidation?.TestsPassed;
                bool testCriterionPassed = testsPassed == true;
                passed &= testCriterionPassed;
                evidenceParts.Add(testsPassed switch
                {
                    true => "Automated tests passed.",
                    false => "Automated tests failed.",
                    _ => "Automated tests were not executed."
                });

                AppendRequiredTestFileEvidence(state, repoPaths, evidenceParts, ref passed, testsPassed);
            }

            evaluations.Add(new AcceptanceCriterionEvaluation(
                criterion.Id,
                criterion.Description,
                passed,
                string.Join(' ', evidenceParts),
                "deterministic"));
            evaluatedCriteria.Add(criterion.Id);
        }

        return new AcceptanceCriteriaReport { Evaluations = evaluations };
    }

    public static AcceptanceCriteriaReport MergeReports(
        AcceptanceCriteriaReport deterministic,
        AcceptanceCriteriaReport? llmReport,
        RequirementsSpec requirements)
    {
        var merged = new Dictionary<string, AcceptanceCriterionEvaluation>(StringComparer.OrdinalIgnoreCase);
        foreach (AcceptanceCriterionEvaluation evaluation in deterministic.Evaluations)
        {
            merged[evaluation.Id] = evaluation;
        }

        if (llmReport is not null)
        {
            foreach (AcceptanceCriterionEvaluation evaluation in llmReport.Evaluations)
            {
                if (merged.TryGetValue(evaluation.Id, out AcceptanceCriterionEvaluation? deterministicEvaluation)
                    && PreferDeterministicOverLlm(deterministicEvaluation, evaluation))
                {
                    continue;
                }

                merged[evaluation.Id] = evaluation;
            }
        }

        foreach (AcceptanceCriterion criterion in requirements.AcceptanceCriteria)
        {
            if (merged.ContainsKey(criterion.Id))
            {
                continue;
            }

            merged[criterion.Id] = new AcceptanceCriterionEvaluation(
                criterion.Id,
                criterion.Description,
                false,
                "No deterministic or LLM evaluation was produced for this criterion.",
                "missing");
        }

        return new AcceptanceCriteriaReport
        {
            Evaluations = merged.Values
                .OrderBy(evaluation => evaluation.Id, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    public static bool HasBlockingFailures(AcceptanceCriteriaReport report) =>
        !report.AllPassed;

    public static string FormatGateDiagnostics(AcceptanceCriteriaReport report) =>
        $"Acceptance criteria gate: {report.PassedCount}/{report.Evaluations.Count} passed "
        + $"(failed={report.FailedCount}).";

    public static IEnumerable<AgentFinding> ToFindings(AcceptanceCriteriaReport report) =>
        report.Evaluations
            .Where(evaluation => !evaluation.Passed)
            .Select(evaluation => new AgentFinding
            {
                Severity = evaluation.Source is "deterministic" or "missing" ? FindingSeverity.Blocker : FindingSeverity.High,
                Message = $"Acceptance criterion '{evaluation.Id}' failed: {evaluation.Evidence}"
            });

    private static HashSet<string> CollectRepoRelativePaths(WorkflowState state)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(state.RepoPath))
        {
            return paths;
        }

        foreach (string file in Directory.EnumerateFiles(state.RepoPath, "*.*", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}workflowX-output{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                || file.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                || file.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            paths.Add(NormalizePath(Path.GetRelativePath(state.RepoPath, file)));
        }

        return paths;
    }

    private static string NormalizePath(string path) =>
        path.Trim().Replace('\\', '/');

    private static bool MatchesAnyKeyword(string text, IEnumerable<string> keywords)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return keywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static void AppendRequiredTestFileEvidence(
        WorkflowState state,
        HashSet<string> repoPaths,
        List<string> evidenceParts,
        ref bool passed,
        bool? testsPassed)
    {
        IReadOnlyList<string> requiredTestPaths = MissingLayerTestSynthesizer.GetRequiredTestPaths(state);
        if (requiredTestPaths.Count == 0)
        {
            return;
        }

        var presentTestPaths = new List<string>();
        var missingTestPaths = new List<string>();
        foreach (string path in requiredTestPaths)
        {
            if (repoPaths.Contains(NormalizePath(path)))
            {
                presentTestPaths.Add(path);
            }
            else
            {
                missingTestPaths.Add(path);
            }
        }

        if (presentTestPaths.Count > 0)
        {
            evidenceParts.Add($"Required test files on disk: {string.Join(", ", presentTestPaths)}.");
        }

        if (missingTestPaths.Count > 0)
        {
            evidenceParts.Add($"Missing required test files: {string.Join(", ", missingTestPaths)}.");
            if (testsPassed != true)
            {
                passed = false;
            }
        }
    }

    private static bool PreferDeterministicOverLlm(
        AcceptanceCriterionEvaluation deterministic,
        AcceptanceCriterionEvaluation llm)
    {
        if (!string.Equals(deterministic.Source, "deterministic", StringComparison.OrdinalIgnoreCase)
            || !deterministic.Passed
            || llm.Passed)
        {
            return false;
        }

        return HasAuthoritativeBuildOrTestEvidence(deterministic.Evidence);
    }

    internal static bool HasAuthoritativeBuildOrTestEvidence(string evidence) =>
        evidence.Contains("Automated tests passed.", StringComparison.OrdinalIgnoreCase)
        || evidence.Contains("Production build passed.", StringComparison.OrdinalIgnoreCase);
}
