using Microsoft.SemanticKernel;

sealed class BackendDeveloperAgent : LlmWorkflowAgentBase
{
    private const string JsonOutputSchema = """
        Return strictly valid JSON:
        {
          "summary": "short summary",
          "files": [
            { "path": "relative/path/from/repo/root.ext", "content": "full file content" }
          ]
        }
        """;

    public BackendDeveloperAgent(Kernel kernel) : base(kernel)
    {
    }

    public override string Name => "BackendDeveloperAgent";

    protected override string BuildPrompt(WorkflowState state)
    {
        string architecturePlan = state.Architecture?.Summary ?? string.Empty;
        var requiredPaths = WorkflowFindingRules.ExtractBackendPaths(architecturePlan);
        string checklist = requiredPaths.Count == 0
            ? "(extract file paths and requirements from the architecture section above)"
            : string.Join("\n", requiredPaths.Select(path => $"- {path}"));

        return $"""
            You are the backend developer agent.
            Implement the architecture plan. Use RAG exemplars for all code structure and syntax.

            Task: {state.Task.Title}
            Task detail: {state.Task.Description}

            Architecture plan (requirements only — prose descriptions per file; implement from RAG, not from any sample code):
            {architecturePlan}

            Backend file paths identified from architecture (each must be in files[] with a full implementation matching its description):
            {checklist}

            Rules:
            - Implement every path listed in BACKEND_FILES (and backend tasks described in the architecture plan).
            - Match each file's described responsibilities using the closest layer exemplar in RAG.
            - Return complete source files only: no stubs, TODO, NotImplementedException, or placeholder comments.
            - Every checklist path above must appear in files[].

            Unified RAG context (exemplars and conventions):
            {state.CombinedRagContext}

            {JsonOutputSchema}
            """;
    }

    protected override IReadOnlyList<AgentFinding> BuildFallbackFindings() =>
        new List<AgentFinding>
        {
            new()
            {
                Severity = FindingSeverity.Medium,
                Message = "Backend implementation used fallback guidance; verify generated changes manually."
            }
        };
}
