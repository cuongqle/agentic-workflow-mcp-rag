using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

sealed class RequirementsAgent : LlmWorkflowAgentBase
{
    private const string JsonOutputSchema = """
        Return strictly valid JSON (no markdown fences):
        {
          "userStory": "As a ..., I want ..., so that ...",
          "acceptanceCriteria": [
            { "id": "AC-1", "description": "Given ... When ... Then ..." }
          ],
          "inScope": ["..."],
          "outOfScope": ["..."],
          "risks": ["..."]
        }
        """;

    public RequirementsAgent(Kernel kernel) : base(kernel)
    {
    }

    public override string Name => "RequirementsAgent";

    public override async Task<AgentResult> ExecuteAsync(WorkflowState state, CancellationToken cancellationToken = default)
    {
        string summary;
        RequirementsSpec? spec = null;
        try
        {
            string prompt = BuildPrompt(state);
            var chat = Kernel.GetRequiredService<IChatCompletionService>();
            var history = new ChatHistory();
            history.AddUserMessage(prompt);
            var response = await chat.GetChatMessageContentsAsync(history, cancellationToken: cancellationToken);
            string raw = response.FirstOrDefault()?.Content ?? string.Empty;

            if (RequirementsSpecParser.TryParseJson(raw, out RequirementsSpec? parsedSpec, out summary)
                && parsedSpec is not null)
            {
                spec = parsedSpec;
            }
            else
            {
                summary = raw;
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
            RequirementsSpec = spec,
            Findings = new List<AgentFinding>(BuildFallbackFindings())
        };
    }

    protected override string BuildPrompt(WorkflowState state)
    {
        string repoLayers = WorkflowFindingRules.FormatRepoCapabilities(state);

        return $"""
            You are the requirements agent.
            Convert the task into a testable definition of done before architecture or implementation begins.

            Task title: {state.Task.Title}
            Task detail: {state.Task.Description}
            Repository path: {state.RepoPath}
            Repository layers detected: {repoLayers}

            Unified RAG context:
            {state.CombinedRagContext}

            Rules:
            - Write 3-8 acceptance criteria that are specific, testable, and verifiable from code/build/tests/artifacts.
            - Include at least one criterion about production build success when backend code is expected.
            - Include test-related criteria when the repository has test projects or the task changes behavior.
            - Keep inScope/outOfScope explicit to prevent scope creep.
            - Do not include implementation details or source code — JSON only.

            {JsonOutputSchema}
            """;
    }
}
