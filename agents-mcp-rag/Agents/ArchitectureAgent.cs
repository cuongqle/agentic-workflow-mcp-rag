using Microsoft.SemanticKernel;

sealed class ArchitectureAgent : LlmWorkflowAgentBase
{
    public ArchitectureAgent(Kernel kernel) : base(kernel)
    {
    }

    public override string Name => "ArchitectureAgent";

    protected override string BuildPrompt(WorkflowState state)
    {
        string repoLayers = WorkflowFindingRules.FormatRepoCapabilities(state);

        return $"""
            You are the architecture agent.
            Task title: {state.Task.Title}
            Task detail: {state.Task.Description}
            Repository path: {state.RepoPath}

            Repository layers detected from contract/RAG scan: {repoLayers}
            Only plan deliverables for layers that exist in this repository and are required by the task.

            Unified RAG context:
            {state.CombinedRagContext}

            Specify WHAT to build. Implementer agents decide HOW using RAG exemplars — you do not write code.

            Produce (use these exact section headers):
            1) Architecture changes and rationale
            2) BACKEND_FILES
            3) FRONTEND_FILES
            4) Test strategy and rollback notes

            BACKEND_FILES — required section when backend=yes; at least one line per file (path relative to repo root):
            - path/File.cs: required types, members, and behaviors in plain language
            Omit this section only when backend=no.

            FRONTEND_FILES — required section when frontend=yes; at least one line per file:
            - path/file.js: required module behavior in plain language
            Omit this section only when frontend=no.

            Rules:
            - Implementer agents only run files listed in these sections; empty or missing sections mean no code is generated.
            - Include every file the task requires for each layer that exists in the repository.
            - No code fences (no ```), no source code, no stub comments.
            - Reference exemplar paths from RAG by name only.

            Keep output concise and actionable.
            """;
    }
}
