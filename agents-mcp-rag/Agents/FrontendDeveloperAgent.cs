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

Include real frontend files that should be created/updated in the target repository.
Paths must be repository-relative. Follow the discovered frontend contract and exemplar paths in unified RAG context (layout mode, modules root, subfolders, root files). Do not invent paths that contradict that contract.";
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
