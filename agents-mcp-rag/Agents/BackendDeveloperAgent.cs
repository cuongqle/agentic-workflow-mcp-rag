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
If you create {{Entity}}Repository.cs, you MUST also create {{Entity}}RepositoryTests.cs in the existing RepositoryTest folder (same MSTest style as sibling repository tests).
Do NOT invent generic roots like src/, app/, backend/, frontend/ unless they already exist in legacy context.
Follow 'Critical repository contract (MUST follow exactly)' from unified RAG context.
If repository inheritance or constructor rules are missing, output will be rejected.
Follow the exact existing project paths for repository interfaces and implementations.
If you create {{Entity}}Repository.cs, you MUST also create I{{Entity}}Repository.cs in the existing Interfaces folder.
Interface signature must follow: public interface I{{Entity}}Repository: IRepository<{{Entity}}>.
Repository implementation must be concrete (no TODO/placeholder comments), include real method bodies, and follow existing constructor/base patterns.
Include required using directives consistent with existing repositories (Entities, Interfaces, DbStore, Indexes/Expressions when used).";
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
