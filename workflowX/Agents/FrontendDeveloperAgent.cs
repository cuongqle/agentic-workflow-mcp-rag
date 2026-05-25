using Microsoft.SemanticKernel;

sealed class FrontendDeveloperAgent : LlmWorkflowAgentBase
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

    public FrontendDeveloperAgent(Kernel kernel) : base(kernel)
    {
    }

    public override string Name => "FrontendDeveloperAgent";

    protected override string BuildPrompt(WorkflowState state)
    {
        string architecturePlan = state.Architecture?.Summary ?? string.Empty;
        var requiredPaths = WorkflowFindingRules.GetFrontendPaths(state);
        string checklist = requiredPaths.Count == 0
            ? "(no FRONTEND_FILES paths parsed — read FRONTEND_FILES from the architecture plan above)"
            : string.Join("\n", requiredPaths.Select(path => $"- {path}"));

        return $"""
            You are the frontend developer agent.
            Implement frontend deliverables from the architecture plan only. Use RAG exemplars for module layout.

            Task: {state.Task.Title}
            Task detail: {state.Task.Description}

            Architecture plan:
            {architecturePlan}

            FRONTEND_FILES checklist (every path must be in files[] with full content):
            {checklist}

            Rules:
            - If there is no FRONTEND_FILES section or checklist is empty, return files: [] and a short summary.
            - Otherwise implement every FRONTEND_FILES entry; follow module layout from RAG.
            - Complete source files only.

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
                Message = "Frontend implementation used fallback guidance; verify generated changes manually."
            }
        };
}
