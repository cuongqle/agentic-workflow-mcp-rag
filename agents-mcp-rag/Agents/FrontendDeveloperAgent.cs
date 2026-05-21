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
        var requiredPaths = WorkflowFindingRules.ExtractFrontendPaths(architecturePlan);
        string checklist = requiredPaths.Count == 0
            ? "(extract file paths and requirements from FRONTEND_FILES in the architecture section above)"
            : string.Join("\n", requiredPaths.Select(path => $"- {path}"));

        return $"""
            You are the frontend developer agent.
            Implement the architecture plan. Use RAG exemplars for module layout and syntax.

            Task: {state.Task.Title}
            Task detail: {state.Task.Description}

            Architecture plan (requirements only — implement from RAG, not from any sample code):
            {architecturePlan}

            Frontend file paths from architecture (each must be in files[] with full content matching its description):
            {checklist}

            Rules:
            - Implement every path listed in FRONTEND_FILES (and frontend tasks described in the architecture plan).
            - Follow the discovered module layout in RAG; do not invent new project roots.
            - Return complete source files only.

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
                Message = "Frontend implementation used fallback guidance; verify generated changes manually."
            }
        };
}
