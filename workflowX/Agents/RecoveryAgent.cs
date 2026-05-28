using workflowX.Infrastructure;
using Microsoft.SemanticKernel;

sealed class RecoveryAgent : LlmWorkflowAgentBase
{
    public RecoveryAgent(Kernel kernel) : base(kernel)
    {
    }

    public override string Name => "RecoveryAgent";

    protected override string BuildPrompt(WorkflowState state)
    {
        string allowedFiles = state.CompilationFixAllowedFiles.Count == 0
            ? "- none detected from findings (infer carefully)"
            : string.Join("\n", state.CompilationFixAllowedFiles.Select(path => $"- {path}"));
        string complianceIssues = state.ComplianceIssues.Count > 0
            ? string.Join("\n", state.ComplianceIssues.Select(i => $"- {i}"))
            : "- none";
        string buildErrors = FormatBuildFindings(state);
        string contractSummary = state.Contract?.FormatStructureSummary() ?? "(contract not available)";
        if (contractSummary.Length > 3_000)
        {
            contractSummary = contractSummary[..3_000] + "\n[contract summary truncated]";
        }

        string exemplarSources = string.IsNullOrWhiteSpace(state.CompilationFixExemplarContext)
            ? "(no exemplar files attached — use allowed files and read patterns from repo)"
            : state.CompilationFixExemplarContext;

        string recoveryRules = BuildRecoveryRules(state);

        return $@"You are the recovery agent.
Goal: fix the build errors below so the repository compiles. Use minimal, safe edits.

Task:
{state.Task.Description}

Repository contract (discovered layout — follow this):
{contractSummary}

Exemplar sources (FULL files from this repo — match patterns here; primary reference):
{exemplarSources}

Build errors (fix every line — compiler output):
{buildErrors}

Allowed files to edit:
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

    private static string BuildRecoveryRules(WorkflowState state)
    {
        RepoStack stack = RepoStack.From(state);
        var lines = new List<string>
        {
            "Fix every build error and every apply rejection. Read each rejection reason literally.",
            "Edit only allowed files unless impossible.",
            "Never edit obj/ or bin/ paths."
        };

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
