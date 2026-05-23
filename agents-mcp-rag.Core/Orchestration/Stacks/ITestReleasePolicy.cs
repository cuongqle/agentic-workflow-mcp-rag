namespace agents_mcp_rag.Orchestration.Stacks;

/// <summary>
/// Stack-specific test quarantine and release policy (e.g. DotNet *Tests.cs deferral).
/// </summary>
public interface ITestReleasePolicy
{
    bool ShouldAttemptQuarantine(WorkflowState state);

    Task<bool> TryQuarantineAsync(
        WorkflowState state,
        Dictionary<string, AppliedFileChange> rollbackChanges,
        Func<WorkflowState, CancellationToken, Task<AgentResult>> revalidateBuildAsync,
        Func<string, Task> publishStatusAsync,
        CancellationToken cancellationToken);

    void ApplyReleasePolicy(WorkflowState state);
}
