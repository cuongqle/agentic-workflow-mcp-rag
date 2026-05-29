using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

sealed class ArchitectureAgent : LlmWorkflowAgentBase
{
    private const string JsonOutputSchema = """
        Return strictly valid JSON (no markdown fences):
        {
          "summary": "architecture rationale in plain language",
          "backendFiles": [
            { "path": "relative/path/from/repo/root.cs", "description": "Mirror <same-kind exemplar path from RAG>; change only subject — no invented stores or APIs" }
          ],
          "frontendFiles": [
            { "path": "relative/path/from/repo/root.js", "description": "required module behavior" }
          ],
          "testStrategy": "how to validate the change",
          "rollbackNotes": "how to revert safely"
        }
        """;

    public ArchitectureAgent(Kernel kernel) : base(kernel)
    {
    }

    public override string Name => "ArchitectureAgent";

    public override async Task<AgentResult> ExecuteAsync(WorkflowState state, CancellationToken cancellationToken = default)
    {
        string summary;
        ArchitecturePlan? plan = null;
        try
        {
            string prompt = BuildPrompt(state);
            var chat = Kernel.GetRequiredService<IChatCompletionService>();
            var history = new ChatHistory();
            history.AddUserMessage(prompt);
            var response = await chat.GetChatMessageContentsAsync(history, cancellationToken: cancellationToken);
            string raw = response.FirstOrDefault()?.Content ?? string.Empty;

            if (ArchitecturePlanParser.TryParseJson(raw, out ArchitecturePlan? parsedPlan, out summary)
                && parsedPlan is not null)
            {
                plan = parsedPlan;
            }
            else
            {
                summary = raw;
                plan = ArchitecturePlanParser.ParseMarkdown(raw);
                if (plan is not null)
                {
                    summary = ArchitecturePlanParser.FormatReadableSummary(plan);
                }
            }
        }
        catch (Exception ex)
        {
            summary = $"Fallback output because LLM call failed in {Name}: {ex.Message}";
        }

        return new AgentResult
        {
            AgentName = Name,
            Summary = summary,
            ArchitecturePlan = plan,
            Findings = new List<AgentFinding>(BuildFallbackFindings())
        };
    }

    protected override string BuildPrompt(WorkflowState state)
    {
        string repoLayers = WorkflowFindingRules.FormatRepoCapabilities(state);
        string requirementsSummary = state.RequirementsSpec is not null
            ? RequirementsSpecParser.FormatReadableSummary(state.RequirementsSpec)
            : state.Requirements?.Summary ?? "(no structured requirements available)";

        return $"""
            You are the architecture agent.
            Task title: {state.Task.Title}
            Task detail (may mention technologies not used in this repo — do not copy those into summary or descriptions; plan only from RAG exemplars):
            {state.Task.Description}
            Repository path: {state.RepoPath}

            Requirements and acceptance criteria (exemplar paths in RAG override requirement wording about storage):
            {requirementsSummary}

            Repository layers detected from contract/RAG scan: {repoLayers}
            Only plan deliverables for layers that exist in this repository and are required by the task.

            Unified RAG context:
            {state.CombinedRagContext}

            Specify WHAT to build. Implementer agents decide HOW using RAG exemplars — you do not write code.

            Before backendFiles: read "Grouped folder exemplars", "Production source exemplars", and "Solution projects" in RAG. Each planned path must sit in the same folder group as a same-kind on-disk exemplar — change only the file name. Do not invent files when no same-kind exemplar exists. Never prefix paths with an extra copy of the repo or solution root folder.

            Rules:
            - Satisfy every acceptance criterion from requirements intake.
            - Use backendFiles only when backend=yes; use frontendFiles only when frontend=yes.
            - Include every file the task requires for each active stack area (backend/frontend).
            - Paths must be relative to the repository root and include the correct file extension.
            - Do not include source code, stubs, or markdown — JSON only.
            {FormatCSharpRules()}
            - Plan production paths with the same folder segments as RAG exemplars (one solution-project directory per path); never prefix with an extra copy of the repository or solution root folder.
            - Never plan AssemblyInfo.cs or generated artifact paths under obj/ or bin/.
            - If no same-kind exemplar exists in RAG for a required deliverable, omit that backendFiles entry and state low confidence in summary.

            {JsonOutputSchema}
            """;
    }

    private static string FormatCSharpRules() =>
        string.Join("\n", CSharpPromptSupport.BuildArchitectureAgentRuleLines().Select(rule => $"- {rule}"));

}
