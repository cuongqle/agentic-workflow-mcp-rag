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

        string exemplarSources = string.IsNullOrWhiteSpace(state.ImplementationExemplarContext)
            ? "(no same-kind exemplar files attached — use Unified RAG path lists and semantic hits)"
            : state.ImplementationExemplarContext;

        return $"""
            You are the backend developer agent.
            Implement backend deliverables from the architecture plan only. Mirror same-kind exemplar sources below.

            Task: {state.Task.Title}
            Task detail (for scope only — do not implement types or stores named here unless they appear in Exemplar sources):
            {state.Task.Description}

            Architecture plan (paths/checklist only — ignore narrative descriptions that name types not in Exemplar sources):
            {architecturePlan}

            BACKEND_FILES checklist (ONLY these paths may appear in files[] — apply rejects anything else):
            {checklist}

            Rules:
            - If the checklist is empty, return files: [] and a short summary.
            - Implement every BACKEND_FILES entry with full source; for each path, copy implementation structure from a same-kind exemplar in Exemplar sources (constructor, injected dependencies, calls, usings, async style).
            {FormatBackendScopeRules()}
            - When the checklist includes *Tests.cs files, implement every one in the same JSON batch as production files; mirror the attached test exemplar (path, class name = file name, usings, fixtures).
            - When the checklist includes a test .csproj, return it with PackageReference/ProjectReference and TargetFramework copied verbatim from the test exemplar in RAG (never invent or downgrade TargetFramework).
            - When the checklist includes a test project file, return that file (full content) with package, project references, and TargetFramework copied from RAG.
            - Complete source only: no stubs, TODO, NotImplementedException, placeholder comments, or "// Add methods ... if needed".
            - Copy paths exactly as listed on the checklist — same project folder segments as RAG exemplars; never add an extra leading repository folder segment.
            - Never return AssemblyInfo.cs, *.AssemblyInfo.cs, or files under obj/ or bin/.
            - Copy the full using block from Exemplar sources for each checklist path (including 'Required usings and namespaces'); add `using` for every cross-deliverable type in a different namespace — ProjectReference does not import namespaces.
            - Keep data types consistent across every deliverable you touch: mirror RAG exemplars end-to-end.
            - At HTTP boundary string inputs, convert to dependency parameter types before calling (TryParse + validation response when needed).
            - Use Parse/TryParse only when converting from string input.
            - Never edit protected pre-existing infrastructure contracts; adapt to them.
            - For Create/Update/Post actions, resolve each *Id foreign key through the injected dependency the same way same-kind exemplars do before persist/update.
            - For DI wiring, edit only existing composition-root registration blocks; append interface-to-implementation pairs only.
            - In tests, use correctly typed temporal values matching on-disk model definitions.
            {FormatCSharpRules()}
            {FormatTestPackageRulesWhenListed(checklist)}

            Pre-return checklist:
            - files[] contains ONLY paths from BACKEND_FILES (plus allowed companion I* interfaces for listed implementations).
            - No path in files[] is missing from the checklist and no checklist path is missing from files[] (unless intentionally skipped with reason in summary).
            - Every path matches the checklist string exactly (no duplicated repo/solution folder prefix).
            - Each .cs file includes every `using` listed for that path under Required usings and namespaces in Exemplar sources.
            - No comments or summaries that reference storage products or APIs not present in the same-kind exemplar file you copied.

            Exemplar sources (full on-disk same-kind files — sole source of types, usings, and calls):
            {exemplarSources}

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
