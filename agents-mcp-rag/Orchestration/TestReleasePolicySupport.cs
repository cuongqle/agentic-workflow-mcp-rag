using agents_mcp_rag.Infrastructure;

/// <summary>
/// Composite test quarantine and release policy — delegates to registered stack modules.
/// </summary>
static class TestReleasePolicySupport
{
    public static bool ShouldAttemptQuarantine(WorkflowState state)
    {
        StackModuleRegistration.RegisterDefaults();
        RepoStack stack = RepoStack.From(state);
        return StackModuleRegistry.TestReleasePolicies(stack)
            .Any(policy => policy.ShouldAttemptQuarantine(state));
    }

    public static async Task<bool> TryQuarantineAsync(
        WorkflowState state,
        Dictionary<string, AppliedFileChange> rollbackChanges,
        Func<WorkflowState, CancellationToken, Task<AgentResult>> revalidateBuildAsync,
        Func<string, Task> publishStatusAsync,
        CancellationToken cancellationToken)
    {
        StackModuleRegistration.RegisterDefaults();
        RepoStack stack = RepoStack.From(state);
        bool quarantined = false;
        foreach (ITestReleasePolicy policy in StackModuleRegistry.TestReleasePolicies(stack))
        {
            quarantined |= await policy.TryQuarantineAsync(
                state,
                rollbackChanges,
                revalidateBuildAsync,
                publishStatusAsync,
                cancellationToken);
        }

        return quarantined;
    }

    public static void ApplyReleasePolicy(WorkflowState state)
    {
        StackModuleRegistration.RegisterDefaults();
        RepoStack stack = RepoStack.From(state);
        foreach (ITestReleasePolicy policy in StackModuleRegistry.TestReleasePolicies(stack))
        {
            policy.ApplyReleasePolicy(state);
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
