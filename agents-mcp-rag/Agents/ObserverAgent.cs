using Microsoft.SemanticKernel;

sealed class ObserverAgent : LlmWorkflowAgentBase
{
    public ObserverAgent(Kernel kernel) : base(kernel)
    {
    }

    public override string Name => "ObserverAgent";

    protected override string BuildPrompt(WorkflowState state)
    {
        return $"""
            You are the observer agent.
            Monitor workflow drift and integration risk.

            Architecture summary:
            {state.Architecture?.Summary}

            Backend summary:
            {state.Backend?.Summary}

            Frontend summary:
            {state.Frontend?.Summary}

            Unified RAG context:
            {state.CombinedRagContext}

            Output:
            - drift risks
            - coupling risks
            - missing validation points
            """;
    }
}
