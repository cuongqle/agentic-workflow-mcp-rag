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
        string allowedFiles = state.CompilationFixAllowedFiles.Count == 0
            ? "- none detected from findings (infer paths from RAG/architecture when adding tests)"
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
        string recoveryRules = BuildRecoveryRules(state, blockingAudit);
        string goal = blockingAudit
            ? "address blocking audit findings (including missing tests) and fix build errors below. Use minimal, safe edits."
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

Unified RAG context (copy *Tests.cs / spec paths from exemplars here — same folders, new entity name only):
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

Files already applied this run:
{string.Join(", ", state.AppliedFiles)}

""";

        return $@"You are the recovery agent.
Goal: {goal}

Task:
{state.Task.Description}

Repository contract (discovered layout — follow this):
{contractSummary}
{ragSection}{architectureSection}{appliedSection}
Exemplar sources (FULL files from this repo — match patterns here; primary reference):
{exemplarSources}
{auditSection}
Build errors (fix every line — compiler output):
{buildErrors}

Allowed files to edit (build/apply scope — you may still add new test/spec files when audit requires them):
{allowedFiles}

Apply rejections (must fix — files were not written to disk; the workflow will retry until these are resolved or attempts are exhausted):
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

    private static string BuildRecoveryRules(WorkflowState state, bool blockingAudit)
    {
        RepoStack stack = RepoStack.From(state);
        var lines = new List<string>();
        if (blockingAudit)
        {
            lines.AddRange(
            [
                "When audit findings mention missing tests, add *Tests.cs (backend) or .spec./.test. files (frontend) before other fixes.",
                "Infer test paths only from Unified RAG exemplars and the architecture plan: copy an existing test file path for the same layer, change only the entity/file name (never invent .Api/.Application project folders).",
                "You may return new test files not listed under Allowed files and not yet on disk.",
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
            "When returning a new controller/repository/test file, place it in the same solution project folder as existing exemplars for that layer — never create a parallel project directory.",
            "If a required fix would modify a protected on-disk contract file, stop and report low confidence in summary instead of forcing a rewrite.",
            blockingAudit
                ? "For production code, only overwrite files listed in compiler errors; create missing test/spec files from audit even when not on disk."
                : "Only overwrite existing files when that exact path appears in the compiler error list; otherwise change callers/feature code instead.",
            "Workflow: (1) read audit/build findings, (2) identify root cause, (3) mirror RAG patterns, (4) apply minimal fix, (5) stop when confidence is low."
        ]);

        if (stack.DotNet)
        {
            lines.AddRange(CSharpRecoveryPromptSupport.BuildRuleLines());
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
