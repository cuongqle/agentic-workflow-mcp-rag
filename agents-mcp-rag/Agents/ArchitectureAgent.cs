using Microsoft.SemanticKernel;

sealed class ArchitectureAgent : LlmWorkflowAgentBase
{
    public ArchitectureAgent(Kernel kernel) : base(kernel)
    {
    }

    public override string Name => "ArchitectureAgent";

    protected override string BuildPrompt(WorkflowState state)
    {
        return $"""
            You are the architecture agent.
            Task title: {state.Task.Title}
            Task detail: {state.Task.Description}
            Repository path: {state.RepoPath}
            Unified RAG context:
            {state.CombinedRagContext}

            Produce:
            1) Architecture changes and rationale
            2) Backend task list
            3) Frontend task list
            4) Test strategy and rollback notes
            Keep output concise and actionable.
            """;
    }
}
