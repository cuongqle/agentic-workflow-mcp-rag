using workflowX.Infrastructure;
using Microsoft.SemanticKernel;

sealed class RecoveryAgent : LlmWorkflowAgentBase
{
    private const int MaxRagContextChars = 8_000;
    private const int MaxArchitectureChars = 4_000;

    public RecoveryAgent(Kernel kernel) : base(kernel)
    {
    }

    public override string Name => "RecoveryAgent";

    protected override string BuildPrompt(WorkflowState state)
    {
        bool testFocusedRecovery = MissingTestRecoverySupport.ShouldFocusOnMissingTests(state);
        bool restrictAllowedFiles = RecoveryContextSupport.ShouldRestrictAllowedFilesToBuildErrors(state);
        string testChecklist = MissingTestRecoverySupport.BuildPromptSection(state);
        string allowedFiles = state.CompilationFixAllowedFiles.Count == 0
            ? restrictAllowedFiles
                ? "- none (add NEW test/spec files from Test recovery checklist and RAG; do not return existing production files)"
                : "- none detected from findings (infer paths from RAG/architecture when adding tests)"
            : string.Join("\n", state.CompilationFixAllowedFiles.Select(path => $"- {path}"));
        string complianceIssues = state.ComplianceIssues.Count > 0
            ? string.Join("\n", state.ComplianceIssues.Select(i => $"- {i}"))
            : "- none";
        string buildErrors = FormatBuildFindings(state);
        string contractSummary = Truncate(state.Contract?.FormatStructureSummary() ?? "(contract not available)", 3_000);

        string exemplarSources = string.IsNullOrWhiteSpace(state.CompilationFixExemplarContext)
            ? "(no exemplar files attached — use RAG and allowed files)"
            : state.CompilationFixExemplarContext;

        bool blockingAudit = AuditorAgent.HasBlockingFindings(state.Audit);
        IReadOnlyList<AgentFinding> auditFindings = GetRecoveryAuditFindings(state);
        string recoveryRules = BuildRecoveryRules(state, blockingAudit, testFocusedRecovery);
        string goal = testFocusedRecovery
            ? "add every path in the Test recovery checklist (and fix dotnet test / build errors below). Use minimal, safe edits."
            : blockingAudit
                ? "address blocking audit findings and fix build errors below. Use minimal, safe edits."
                : "fix the build errors below so the repository compiles. Use minimal, safe edits.";

        string auditSection = blockingAudit
            ? $"""

Blocking audit findings (must fix — workflow retries until resolved or attempts exhausted):
{FormatFindings(auditFindings)}

"""
            : string.Empty;

        string ragSection = string.IsNullOrWhiteSpace(state.CombinedRagContext)
            ? string.Empty
            : $"""

Unified RAG context (copy test/spec paths from exemplars here — same folders, new feature name only):
{Truncate(state.CombinedRagContext, MaxRagContextChars)}

""";

        string architectureSection = string.IsNullOrWhiteSpace(state.Architecture?.Summary)
            ? string.Empty
            : $"""

Architecture plan (planned paths — implement missing test entries from BACKEND_FILES / FRONTEND_FILES):
{Truncate(state.Architecture.Summary, MaxArchitectureChars)}

""";

        string appliedSection = state.AppliedFiles.Count == 0
            ? string.Empty
            : $"""

Files already applied this run (do not re-return unless Build errors reference the same path):
{string.Join(", ", state.AppliedFiles)}

""";

        return $@"You are the recovery agent.
Goal: {goal}

Task:
{state.Task.Description}

Repository contract (discovered layout — follow this):
{contractSummary}
{testChecklist}{ragSection}{architectureSection}{appliedSection}
Exemplar sources (FULL files from this repo — match patterns here; primary reference):
{exemplarSources}
{auditSection}
Build errors (fix every line — compiler output):
{buildErrors}

Allowed files to edit (copy paths character-for-character — includes compiler-referenced paths and owning .csproj when a .cs file in that project is listed):
{allowedFiles}

Path shape (apply rejects duplicated repo folder — do not use the WRONG form):
- WRONG: RepoName/RepoName.ProjectFolder/SomeFile.ext
- RIGHT: RepoName.ProjectFolder/SomeFile.ext

Apply rejections (must fix — files were not written to disk; re-return each file only at the canonical path from Allowed files or build output):
{complianceIssues}

Rules:
{recoveryRules}

IMPORTANT: Return strictly valid JSON with this shape:
{{
  ""summary"": ""short recovery summary"",
  ""files"": [
    {{
      ""path"": ""relative/path/from/repo/root.ext"",
      ""content"": ""full fixed file content""
    }}
  ]
}}";
    }

    private static string BuildRecoveryRules(WorkflowState state, bool blockingAudit, bool testFocusedRecovery)
    {
        RepoStack stack = RepoStack.From(state);
        var lines = new List<string>();
        if (testFocusedRecovery)
        {
            lines.AddRange(
            [
                "This pass is test-focused: implement every path in Test recovery checklist before other edits.",
                "Copy test file paths only from Unified RAG test exemplars (same test project directory and subfolders as exemplar; never production project folders).",
                "Never create a new test .csproj or test solution folder; update only test projects listed in RAG \"Test project references\".",
                "Return full content for each new test file; mirror sibling test structure (fixtures, mocks, assertions, using directives).",
                "Do NOT return production files from Files already applied unless Build errors name that exact path.",
                "After tests exist on disk, fix any test compile errors named in Build errors (then production only if explicitly referenced).",
                "When build output says a package type or namespace could not be found, return the exemplar test project file from RAG with that package reference — the failing test source may be in the wrong project folder.",
                "When build output says a type from a referenced project could not be found, add `using` for the exact namespace from that type's definition in Exemplar sources; copy usings from sibling test exemplars when available.",
                "When build output reports duplicate assembly attributes, remove duplicate assembly metadata from hand-written source while the SDK also generates it — never return or edit generated artifact folders.",
                "Never return production files under dotted namespace folders or under the wrong solution-project directory — copy paths from same-kind RAG exemplars.",
                "Always return the owning test project file (full content) in files[] together with every test source file you add or fix.",
                "Use the test project file path exactly as listed in Allowed files or RAG Test project references — never add an extra leading repository folder segment to that path.",
                "When returning a test .csproj, copy TargetFramework exactly from RAG Test project references — never downgrade (e.g. to net5.0); NU1201 means TFM mismatch with a referenced production project."
            ]);
        }
        else if (blockingAudit)
        {
            lines.AddRange(
            [
                "When audit findings mention missing tests, add test source files (backend) or spec/test files (frontend) before other fixes.",
                "Infer test paths only from Unified RAG exemplars and the architecture plan: copy an existing test file path for the same deliverable kind, change only the feature/file name (never invent solution project folders not listed in RAG).",
                "You may return new test files not listed under Allowed files and not yet on disk.",
                "Do NOT return production files already listed under Files already applied unless Build errors name that exact path — apply will reject other overwrites.",
                "Return ONLY: (1) new test/spec files for audit, (2) production files explicitly named in Build errors. Omit other production files when build passed.",
                "Use repo-relative paths from RAG solution projects only — never duplicate the solution folder (wrong: RepoName/RepoName.ProjectFolder/...; right: RepoName.ProjectFolder/...).",
                "After missing tests are addressed, fix remaining build errors and apply rejections."
            ]);
        }
        else
        {
            lines.Add("Fix ONLY reported build failures and apply rejections.");
        }

        lines.AddRange(
        [
            "Preserve architecture and naming conventions from the repository contract and exemplars.",
            "Mirror neighboring modules/patterns before introducing new structure.",
            "Do not rewrite unrelated code; produce minimal patches only.",
            "Never repeat a previously failed fix attempt.",
            "Use full repo-relative paths; when duplicate filenames exist, never return a bare filename.",
            "When returning a new file, place it in the same solution project folder as same-kind exemplars in RAG — never create a parallel project directory.",
            "If a required fix would modify a protected on-disk contract file, stop and report low confidence in summary instead of forcing a rewrite.",
            testFocusedRecovery || blockingAudit
                ? "For production code, only overwrite files listed in compiler errors; create missing test/spec files from checklist/RAG even when not on disk."
                : "Only overwrite existing files when that exact path appears in the compiler error list; otherwise change callers/feature code instead.",
            "Workflow: (1) read audit/build findings, (2) identify root cause, (3) mirror RAG patterns, (4) apply minimal fix, (5) stop when confidence is low."
        ]);

        if (stack.DotNet)
        {
            lines.AddRange(CSharpPromptSupport.BuildRecoveryRuleLines());
            if (testFocusedRecovery || blockingAudit || HasTestPackageCompileErrors(state))
            {
                lines.AddRange(TestProjectPackagePromptSupport.BuildRuleLines());
            }
        }

        if (stack.Frontend)
        {
            lines.AddRange(FrontendRecoveryPromptSupport.BuildRuleLines());
        }

        if (!stack.DotNet && !stack.Frontend)
        {
            lines.Add(
                "Return complete source files for the paths you edit (balanced delimiters; no truncation).");
        }

        return string.Join("\n", lines.Select(line => $"- {line}"));
    }

    private static IReadOnlyList<AgentFinding> GetRecoveryAuditFindings(WorkflowState state)
    {
        if (state.Audit?.Findings is null || state.Audit.Findings.Count == 0)
        {
            return Array.Empty<AgentFinding>();
        }

        return state.Audit.Findings
            .Where(finding => finding.Severity is FindingSeverity.High or FindingSeverity.Blocker)
            .Where(finding => !IsCompilerFinding(finding.Message))
            .GroupBy(finding => finding.Message, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static bool IsCompilerFinding(string message) =>
        message.Contains("error CS", StringComparison.Ordinal)
        || message.Contains(": error ", StringComparison.OrdinalIgnoreCase);

    private static bool HasTestPackageCompileErrors(WorkflowState state)
    {
        foreach (AgentFinding finding in state.BuildValidation?.Findings ?? Enumerable.Empty<AgentFinding>())
        {
            if (!BuildFailureClassifier.ReportsUnresolvedTypeOrNamespace(finding.Message))
            {
                continue;
            }

            foreach (string path in BuildFailureClassifier.CollectSourcePathsFromMessage(finding.Message, state.RepoPath))
            {
                if (TestProjectPathSupport.IsTestSourcePath(state.RepoPath, path))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string FormatFindings(IReadOnlyList<AgentFinding> findings) =>
        findings.Count == 0
            ? "- (no non-compiler audit findings — still honor audit summary and missing-test rules above)"
            : string.Join('\n', findings.Select(finding => $"- [{finding.Severity}] {finding.Message}"));

    private static string FormatBuildFindings(WorkflowState state)
    {
        IReadOnlyList<AgentFinding>? findings = state.BuildValidation?.Findings;
        if (findings is null || findings.Count == 0)
        {
            return "- (no build findings)";
        }

        RepoStack stack = RepoStack.From(state);
        var lines = new List<string>();
        foreach (var finding in findings)
        {
            if (stack.DotNet && BuildFailureClassifier.IsSummaryBanner(finding.Message))
            {
                continue;
            }

            if (!stack.DotNet && finding.Severity is not (FindingSeverity.High or FindingSeverity.Blocker))
            {
                continue;
            }

            lines.Add($"- {finding.Message}");
        }

        return lines.Count == 0 ? "- (no detailed errors)" : string.Join('\n', lines);
    }

    private static string Truncate(string value, int maxChars) =>
        value.Length <= maxChars ? value : value[..maxChars] + "\n[truncated]";

    protected override IReadOnlyList<AgentFinding> BuildFallbackFindings()
    {
        return new List<AgentFinding>
        {
            new()
            {
                Severity = FindingSeverity.Low,
                Message = "Recovery plan was generated with fallback output."
            }
        };
    }
}
