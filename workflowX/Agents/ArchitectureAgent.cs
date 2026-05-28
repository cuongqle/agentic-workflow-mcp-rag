using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

sealed class ArchitectureAgent : LlmWorkflowAgentBase
{
    private const string JsonOutputSchema = """
        Return strictly valid JSON (no markdown fences):
        {
          "summary": "architecture rationale in plain language",
          "backendFiles": [
            { "path": "relative/path/from/repo/root.cs", "description": "required types, members, behaviors" }
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
            Task detail: {state.Task.Description}
            Repository path: {state.RepoPath}

            Requirements and acceptance criteria:
            {requirementsSummary}

            Repository layers detected from contract/RAG scan: {repoLayers}
            Only plan deliverables for layers that exist in this repository and are required by the task.

            Unified RAG context:
            {state.CombinedRagContext}

            Specify WHAT to build. Implementer agents decide HOW using RAG exemplars — you do not write code.

            Rules:
            - Satisfy every acceptance criterion from requirements intake.
            - Use backendFiles only when backend=yes; use frontendFiles only when frontend=yes.
            - Include every file the task requires for each active layer.
            - Paths must be relative to the repository root and include the correct file extension.
            - Do not include source code, stubs, or markdown — JSON only.
            - For every backendFiles.path: copy an existing exemplar path from RAG for the same layer and change only the file name.
            - Controllers/repositories/entities/interfaces: same project segment and folder as matching exemplars in RAG.
            - Tests: use the same project segment as existing *Tests.cs exemplars in RAG.
            - Use only solution project directory names listed in RAG; never invent folders (no .Api/.Application when the solution has .WebAPI).
            - Never repeat the top-level repository folder as the project folder.
            - If no exemplar exists for a layer in RAG, omit that backendFiles entry and state low confidence in summary.

            {JsonOutputSchema}
            """;
    }
}
