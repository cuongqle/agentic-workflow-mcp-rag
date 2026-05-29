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
            ? "(no BACKEND_FILES paths parsed — return files: [] ; do not invent paths)"
            : string.Join("\n", requiredPaths.Distinct(StringComparer.OrdinalIgnoreCase).Select(path => $"- {path}"));

        return $"""
            You are the backend developer agent.
            Implement backend deliverables from the architecture plan only. Use RAG exemplars for code structure.

            Task: {state.Task.Title}
            Task detail: {state.Task.Description}

            Architecture plan:
            {architecturePlan}

            BACKEND_FILES checklist (ONLY these paths may appear in files[] — apply rejects anything else):
            {checklist}

            Rules:
            - If the checklist is empty, return files: [] and a short summary.
            - Implement every BACKEND_FILES entry with full source; match responsibilities using RAG exemplars.
            {FormatBackendScopeRules()}
            - When the checklist includes *Tests.cs, implement tests by mirroring same-layer exemplars from RAG.
            - When the checklist includes a test .csproj, return that .csproj (full file) with PackageReference/ProjectReference copied from RAG.
            - Complete source only: no stubs, TODO, NotImplementedException, placeholder comments, or "// Add methods ... if needed".
            - Copy paths exactly as listed on the checklist — same project folder segments as RAG exemplars; never add an extra leading repository folder segment.
            - Never return AssemblyInfo.cs, *.AssemblyInfo.cs, or files under obj/ or bin/.
            - For every type used from a referenced project, add `using` for the exact namespace from Exemplar sources (or copy usings from sibling *Tests.cs exemplars).
            - Keep data types consistent across every layer and file you touch: mirror RAG exemplars end-to-end.
            - At API boundaries ([FromQuery]/[FromRoute]/request DTO strings), convert to dependency parameter types before calling (TryParse + validation response when needed).
            - Use Parse/TryParse only when converting from string input.
            - Never edit protected pre-existing infrastructure contracts; adapt to them.
            - Call only methods declared on injected interfaces; implement every interface member with matching signatures.
            - For Create/Update/Post actions, resolve each entity *Id foreign key through the injected dependency before persist/update.
            - For DI wiring, edit only existing composition-root registration blocks; append interface-to-implementation pairs only.
            - In tests, use correctly typed temporal values matching on-disk model definitions.
            {FormatCSharpRules()}
            {FormatTestPackageRulesWhenListed(checklist)}

            Pre-return checklist:
            - files[] contains ONLY paths from BACKEND_FILES (plus allowed companion I* interfaces for listed implementations).
            - No path in files[] is missing from the checklist and no checklist path is missing from files[] (unless intentionally skipped with reason in summary).
            - Every path matches the checklist string exactly (no duplicated repo/solution folder prefix).

            Unified RAG context:
            {state.CombinedRagContext}

            {JsonOutputSchema}
            """;
    }

    private static string FormatBackendScopeRules() =>
        string.Join("\n", CSharpPromptSupport.BuildBackendImplementerScopeRuleLines().Select(rule => $"- {rule}"));

    private static string FormatCSharpRules() =>
        string.Join("\n", CSharpPromptSupport.BuildPlacementAndTestRuleLines().Select(rule => $"- {rule}"));

    private static string FormatTestPackageRulesWhenListed(string checklist) =>
        checklist.Contains("Tests", StringComparison.OrdinalIgnoreCase)
        || checklist.Contains(".csproj", StringComparison.OrdinalIgnoreCase)
            ? string.Join("\n", TestProjectPackagePromptSupport.BuildRuleLines().Select(rule => $"- {rule}"))
            : string.Empty;

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
