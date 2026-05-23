/// <summary>
/// DotNet test quarantine adapter — delegates to <see cref="DotNetTestReleasePolicySupport"/>.
/// </summary>
sealed class DotNetTestReleasePolicy : ITestReleasePolicy
{
    public bool ShouldAttemptQuarantine(WorkflowState state) =>
        DotNetTestReleasePolicySupport.ShouldAttemptQuarantine(state);

    public Task<bool> TryQuarantineAsync(
        WorkflowState state,
        Dictionary<string, AppliedFileChange> rollbackChanges,
        Func<WorkflowState, CancellationToken, Task<AgentResult>> revalidateBuildAsync,
        Func<string, Task> publishStatusAsync,
        CancellationToken cancellationToken) =>
        DotNetTestReleasePolicySupport.TryQuarantineAsync(
            state,
            rollbackChanges,
            revalidateBuildAsync,
            publishStatusAsync,
            cancellationToken);

    public void ApplyReleasePolicy(WorkflowState state) =>
        DotNetTestReleasePolicySupport.ApplyReleasePolicy(state);
}
