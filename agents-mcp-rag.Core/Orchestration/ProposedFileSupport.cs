namespace agents_mcp_rag.Orchestration;

public static class ProposedFileSupport
{
    public static List<GeneratedFile> GetAllProposedFiles(WorkflowState state) =>
        (state.Backend?.ProposedFiles ?? [])
            .Concat(state.Frontend?.ProposedFiles ?? [])
            .Concat(state.Recovery?.ProposedFiles ?? [])
            .ToList();
}
