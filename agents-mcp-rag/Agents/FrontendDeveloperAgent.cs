using Microsoft.SemanticKernel;

sealed class FrontendDeveloperAgent : LlmWorkflowAgentBase
{
    public FrontendDeveloperAgent(Kernel kernel) : base(kernel)
    {
    }

    public override string Name => "FrontendDeveloperAgent";

    protected override string BuildPrompt(WorkflowState state)
    {
        return $@"You are the frontend developer agent.
Implement frontend scope from architecture plan.

Task: {state.Task.Title}
Architecture:
{state.Architecture?.Summary}
Unified RAG context:
{state.CombinedRagContext}

IMPORTANT: Return strictly valid JSON with this shape:
{{
  ""summary"": ""short frontend summary"",
  ""files"": [
    {{
      ""path"": ""relative/path/from/repo/root.ext"",
      ""content"": ""full file content""
    }}
  ]
}}

Include real frontend files that should be created/updated inside the target repository.
Paths must be repository-relative and should target actual source directories.
Do NOT invent generic roots like src/, app/, backend/, frontend/ unless they already exist in legacy context.";
    }

    protected override IReadOnlyList<AgentFinding> BuildFallbackFindings()
    {
        return new List<AgentFinding>
        {
            new()
            {
                Severity = FindingSeverity.Medium,
                Message = "Frontend implementation used fallback guidance; verify generated changes manually."
            }
        };
    }
}
