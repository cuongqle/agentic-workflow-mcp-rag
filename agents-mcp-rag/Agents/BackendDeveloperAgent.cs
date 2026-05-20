using Microsoft.SemanticKernel;

sealed class BackendDeveloperAgent : LlmWorkflowAgentBase
{
    public BackendDeveloperAgent(Kernel kernel) : base(kernel)
    {
    }

    public override string Name => "BackendDeveloperAgent";

    protected override string BuildPrompt(WorkflowState state)
    {
        return $@"You are the backend developer agent.
Implement backend scope from architecture plan.

Task: {state.Task.Title}
Architecture:
{state.Architecture?.Summary}
Unified RAG context:
{state.CombinedRagContext}

IMPORTANT: Return strictly valid JSON with this shape:
{{
  ""summary"": ""short backend summary"",
  ""files"": [
    {{
      ""path"": ""relative/path/from/repo/root.ext"",
      ""content"": ""full file content""
    }}
  ]
}}

Include real backend files that should be created/updated inside the target repository.
Paths must be repository-relative and should target actual source directories.
Include repository/entity/index/test files when needed to match legacy architecture patterns.
For every new production type you introduce (repository, service, controller, domain class, etc.), add a matching {{Name}}Tests.cs file in the test folder and style already used in this repository when exemplar *Tests.cs files exist for that layer.
Do NOT invent generic roots like src/, app/, backend/, frontend/ unless they already exist in legacy context.
Follow layer contracts and path conventions from unified RAG context.
Use the same project paths, naming, and inheritance patterns as exemplar files in the same layer.
Implementation must be concrete (no TODO/placeholder comments) with real method bodies where the exemplars include them.
Mirror the closest exemplar files in RAG context for the same layer (naming, base types, dependencies, and method bodies). Only call members that exist on the types/interfaces you use.
When adding a Raven index for a new entity, define the entity properties first and map only those properties in the index (see entity+index pair exemplar in RAG context).
When introducing new repository interfaces for this task, append one registration line in the existing test/bootstrap file — do not rewrite the file and do not change pre-existing infrastructure wiring (keep InMemory/factory/lambda registrations exactly as in exemplars).
Do not return bootstrap/DI files unless you are only appending a new interface registration; never replace existing singleton/factory registration lines.
Include required using directives consistent with sibling files in the same layer.";
    }

    protected override IReadOnlyList<AgentFinding> BuildFallbackFindings()
    {
        return new List<AgentFinding>
        {
            new()
            {
                Severity = FindingSeverity.Medium,
                Message = "Backend implementation used fallback guidance; verify generated changes manually."
            }
        };
    }
}
