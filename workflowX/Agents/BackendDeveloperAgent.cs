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
            : string.Join("\n", requiredPaths.Distinct(StringComparer.OrdinalIgnoreCase).Select(path => $"- {path}"));

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
            - If backend production files are listed but *Tests.cs paths are missing, infer and include corresponding test files by mirroring repository test naming and folder conventions from RAG exemplars.
            - Complete source only: no stubs, TODO, NotImplementedException, placeholder comments, or "// Add methods ... if needed".
            - Return only paths listed in BACKEND_FILES (plus companion I* interface files for planned implementations). Do not add .csproj or host entrypoint files unless explicitly listed.
            - Keep data types consistent across every layer and file you touch: mirror RAG exemplars so the same identifier, route/query parameter, property, and method signature use matching types end-to-end (never pass a value through layers with incompatible types).
            - At API boundaries ([FromQuery]/[FromRoute]/request DTO strings), convert to repository/service parameter types before calling dependencies (e.g. int/Guid). Use TryParse-style validation and return BadRequest/validation error when conversion fails.
            - Use Parse/TryParse only when converting from string input; never call int.Parse/Guid.Parse/DateTime.Parse on values already typed as int/Guid/DateTime/etc.
            - Never edit protected pre-existing infrastructure contracts (core store/repository/entity abstractions and shared base infrastructure types); adapt to them.
            - Call only methods declared on injected interfaces; if an interface exposes Insert/Delete (not Add/Remove), use the declared names exactly.
            - Any class implementing an interface must implement all declared interface members with matching method signatures and parameter types.
            - Before returning code, verify every injected-interface call matches the on-disk interface contract exactly (method name, arity, and CLR parameter types).
            - For Create/Update/Post actions, resolve each entity *Id foreign key through the corresponding injected role repository before persisting/updating, and return NotFound (or equivalent) when related records are missing.
            - For DI wiring changes, edit only existing composition-root/bootstrap registration files; add interface-to-implementation registrations inside the existing registration block, mirror nearby lifetime conventions, and do not remove existing registrations.
            - If no clear DI registration block is evident from existing code, do not guess; return low confidence in summary and keep changes minimal.
            - In tests, never assign quoted string literals to non-string model/entity temporal properties; use typed DateTime/DateTimeOffset/DateOnly/TimeOnly values matching on-disk model definitions.
            - Treat *UnitTest*, *Tests*, *Tests.cs, and *.Tests.csproj paths as test artifacts; mirror sibling test folder layout and naming from RAG exemplars.
            - When production files are added, include matching <Subject>Tests.cs files in the repository's existing test conventions.
            {FormatProjectPlacementRules()}

            Unified RAG context:
            {state.CombinedRagContext}

            {JsonOutputSchema}
            """;
    }

    private static string FormatProjectPlacementRules() =>
        string.Join("\n", CSharpProjectPlacementPromptSupport.BuildRuleLines().Select(rule => $"- {rule}"));

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
