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
        var requiredPaths = WorkflowFindingRules.GetBackendPaths(state);
        string checklist = requiredPaths.Count == 0
            ? "(no BACKEND_FILES paths parsed — read BACKEND_FILES from the architecture plan above)"
            : string.Join("\n", requiredPaths.Select(path => $"- {path}"));

        return $"""
            You are the backend developer agent.
            Implement backend deliverables from the architecture plan only. Use RAG exemplars for code structure.

            Task: {state.Task.Title}
            Task detail: {state.Task.Description}

            Architecture plan:
            {architecturePlan}

            BACKEND_FILES checklist (every path must be in files[] with full implementation):
            {checklist}

            Rules:
            - If there is no BACKEND_FILES section or checklist is empty, return files: [] and a short summary.
            - Otherwise implement every BACKEND_FILES entry; match responsibilities using RAG exemplars.
            - Complete source only: no stubs, TODO, NotImplementedException, or placeholder comments.

            Unified RAG context:
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
