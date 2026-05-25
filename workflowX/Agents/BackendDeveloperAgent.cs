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
        var requiredTestPaths = MissingLayerTestSynthesizer.GetRequiredTestPaths(state);
        string checklist = requiredPaths.Count == 0 && requiredTestPaths.Count == 0
            ? "(no BACKEND_FILES paths parsed — read BACKEND_FILES from the architecture plan above)"
            : string.Join("\n", requiredPaths.Concat(requiredTestPaths).Distinct(StringComparer.OrdinalIgnoreCase).Select(path => $"- {path}"));

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
            - When the checklist includes *Tests.cs paths, implement unit tests by mirroring sibling *Tests.cs exemplars from RAG.
            - Complete source only: no stubs, TODO, NotImplementedException, or placeholder comments.
            - Keep CLR types consistent across every layer and file you touch: mirror RAG exemplars so the same identifier, route/query parameter, property, and method signature use matching types end-to-end (never pass a value through layers with incompatible types).
            - For Create/Update/Post actions, mirror existing controller mutation validation: resolve each entity *Id foreign key through the related injected role repository (using whatever lookup member that repository exposes in this repo) before persisting, and return NotFound or an equivalent error when the related record is missing.

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
