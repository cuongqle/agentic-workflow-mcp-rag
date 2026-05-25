using workflowX.Infrastructure;

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
        Dictionary<string, string> pendingApplyRejections)
    {
        if (state.Audit is null)
        {
            return;
        }

        var agentAdvisoryFindings = state.Audit.Findings
            .Where(finding => finding.Severity is FindingSeverity.Low or FindingSeverity.Medium)
            .GroupBy(finding => finding.Message, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        state.Audit.Findings.Clear();
        state.Audit.Findings.AddRange(agentAdvisoryFindings);
        state.Audit.Findings.AddRange(WorkflowFindingRules.CollectComplianceFindings(state, pendingApplyRejections));
        if (state.BuildValidation is not null)
        {
            state.Audit.Findings.AddRange(state.BuildValidation.Findings);
        }

        ApplyReleasePolicy(state);
    }
}
