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

            Specify WHAT to build. Implementer agents decide HOW using RAG exemplars — you do not write code.

            Produce:
            1) Architecture changes and rationale
            2) BACKEND_FILES (required section header)
            3) FRONTEND_FILES (required section header)
            4) Test strategy and rollback notes

            BACKEND_FILES — one line per deliverable (path relative to repo root):
            - path/File.cs: required types, members, and behaviors in plain language

            FRONTEND_FILES — one line per deliverable (path relative to repo root):
            - path/file.js: required module behavior in plain language

            Output policy:
            - No code fences (no ```).
            - No source code, pseudocode, or stub comments.
            - Reference exemplar paths from RAG by name only.

            Keep output concise and actionable.
            """;
    }
}
