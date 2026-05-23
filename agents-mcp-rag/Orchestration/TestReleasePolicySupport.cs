using agents_mcp_rag.Infrastructure;

/// <summary>
/// Routes test quarantine and release policy by <see cref="RepoStack"/>.
/// DotNet: quarantine failing *Tests.cs artifacts when production compiles.
/// Other stacks: no-op until stack-specific policy is added.
/// </summary>
static class TestReleasePolicySupport
{
    public static bool ShouldAttemptQuarantine(WorkflowState state) =>
        RepoStack.From(state).DotNet && DotNetTestReleasePolicySupport.ShouldAttemptQuarantine(state);

    public static Task<bool> TryQuarantineAsync(
        WorkflowState state,
        Dictionary<string, AppliedFileChange> rollbackChanges,
        Func<WorkflowState, CancellationToken, Task<AgentResult>> revalidateBuildAsync,
        Func<string, Task> publishStatusAsync,
        CancellationToken cancellationToken)
    {
        if (!RepoStack.From(state).DotNet)
        {
            return Task.FromResult(false);
        }

        return DotNetTestReleasePolicySupport.TryQuarantineAsync(
            state,
            rollbackChanges,
            revalidateBuildAsync,
            publishStatusAsync,
            cancellationToken);
    }

    public static void ApplyReleasePolicy(WorkflowState state)
    {
        if (RepoStack.From(state).DotNet)
        {
            DotNetTestReleasePolicySupport.ApplyReleasePolicy(state);
        }
    }

    public static void RefreshComplianceAuditFindings(
        WorkflowState state,
        List<AgentFinding> llmOutputQualityFindings)
    {
        if (state.Audit is null)
        {
            return;
        }

        state.Audit.Findings.Clear();
        var refreshedFindings = ContractComplianceValidator.CollectComplianceFindings(state);
        refreshedFindings.AddRange(llmOutputQualityFindings);
        state.Audit.Findings.AddRange(refreshedFindings);
        if (state.BuildValidation is not null)
        {
            state.Audit.Findings.AddRange(state.BuildValidation.Findings);
        }

        ApplyReleasePolicy(state);
    }
}
