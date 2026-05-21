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
        string buildErrors = FormatBuildFindings(state.BuildValidation?.Findings);
        string contractSummary = state.Contract?.FormatStructureSummary() ?? "(contract not available)";
        if (contractSummary.Length > 3_000)
        {
            contractSummary = contractSummary[..3_000] + "\n[contract summary truncated]";
        }

        string exemplarSources = string.IsNullOrWhiteSpace(state.CompilationFixExemplarContext)
            ? "(no exemplar files attached — use allowed files and read patterns from repo)"
            : state.CompilationFixExemplarContext;

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
- Fix every build error and every apply rejection. Read each rejection reason literally (e.g. missing constructor dependency type 'IX') and add that dependency to the constructor.
- When a rejection cites a layer exemplar or missing dependency, open the exemplar source in Exemplar sources and mirror its constructor signature for the target entity.
- Match the exemplar sources above (constructors, interfaces, namespaces, file layout).
- Return complete, valid C# or script files only (balanced braces; no truncation).
- Edit only allowed files unless impossible.
- Never edit obj/ or bin/ paths.
- Do not create new .csproj files unless required.
- Include required using/import directives.

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

    private static string FormatBuildFindings(IReadOnlyList<AgentFinding>? findings)
    {
        if (findings is null || findings.Count == 0)
        {
            return "- (no build findings)";
        }

        var lines = new List<string>();
        foreach (var finding in findings)
        {
            if (finding.Message.Contains("Build FAILED", StringComparison.OrdinalIgnoreCase)
                || finding.Message.Contains("Build failed", StringComparison.OrdinalIgnoreCase))
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
