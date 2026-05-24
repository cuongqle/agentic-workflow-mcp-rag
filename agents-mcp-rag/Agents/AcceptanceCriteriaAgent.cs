using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

sealed class AcceptanceCriteriaAgent : LlmWorkflowAgentBase
{
    private const string JsonOutputSchema = """
        Return strictly valid JSON (no markdown fences):
        {
          "summary": "overall acceptance gate assessment",
          "criteriaResults": [
            { "id": "AC-1", "passed": true, "evidence": "why this criterion is satisfied or not" }
          ]
        }
        """;

    public AcceptanceCriteriaAgent(Kernel kernel) : base(kernel)
    {
    }

    public override string Name => "AcceptanceCriteriaAgent";

    public override async Task<AgentResult> ExecuteAsync(WorkflowState state, CancellationToken cancellationToken = default)
    {
        RequirementsSpec requirements = state.RequirementsSpec ?? new RequirementsSpec();
        string summary;
        AcceptanceCriteriaReport? report = null;
        try
        {
            string prompt = BuildEvaluationPrompt(state, requirements);
            var chat = Kernel.GetRequiredService<IChatCompletionService>();
            var history = new ChatHistory();
            history.AddUserMessage(prompt);
            var response = await chat.GetChatMessageContentsAsync(history, cancellationToken: cancellationToken);
            string raw = response.FirstOrDefault()?.Content ?? string.Empty;

            if (AcceptanceCriteriaReportParser.TryParseJson(raw, requirements, out AcceptanceCriteriaReport? parsedReport, out summary)
                && parsedReport is not null)
            {
                report = parsedReport;
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
            AcceptanceCriteriaReport = report,
            Findings = new List<AgentFinding>(BuildFallbackFindings())
        };
    }

    protected override string BuildPrompt(WorkflowState state)
    {
        RequirementsSpec requirements = state.RequirementsSpec ?? new RequirementsSpec();
        return BuildEvaluationPrompt(state, requirements);
    }

    private static string BuildEvaluationPrompt(WorkflowState state, RequirementsSpec requirements)
    {
        string criteriaList = requirements.AcceptanceCriteria.Count == 0
            ? "(none)"
            : string.Join("\n", requirements.AcceptanceCriteria.Select(criterion => $"- {criterion.Id}: {criterion.Description}"));

        string appliedFiles = string.Join(
            ", ",
            WorkflowFindingRules.GetAllProposedFiles(state).Select(file => file.RelativePath));

        return $"""
            You are the acceptance criteria agent.
            Decide whether the implemented change satisfies each acceptance criterion.

            User story:
            {requirements.UserStory}

            Acceptance criteria:
            {criteriaList}

            Architecture summary:
            {state.Architecture?.Summary}

            Backend summary:
            {state.Backend?.Summary}

            Frontend summary:
            {state.Frontend?.Summary}

            Build validation summary:
            {state.BuildValidation?.Summary}
            Production build passed: {state.BuildValidation?.ProductionBuildPassed}
            Tests passed: {state.BuildValidation?.TestsPassed}

            Observer summary:
            {state.Observer?.Summary}

            Audit summary:
            {state.Audit?.Summary}

            Generated/applied file paths:
            {appliedFiles}

            Rules:
            - Evaluate every listed acceptance criterion by id.
            - Mark passed=false when evidence is missing, ambiguous, or contradicted by build/audit summaries.
            - Use deterministic build/test outcomes as strong evidence.
            - Return JSON only.

            {JsonOutputSchema}
            """;
    }

    protected override IReadOnlyList<AgentFinding> BuildFallbackFindings()
    {
        return
        [
            new AgentFinding
            {
                Severity = FindingSeverity.High,
                Message = "Acceptance criteria evaluation ran in fallback mode; manual verification required before merge."
            }
        ];
    }
}
