using workflowX.Configuration;

sealed partial class WorkflowOrchestrator
{
    private List<string> CollectPullRequestBlockers(WorkflowState state)
    {
        var blockers = new List<string>();

        if (state.Requirements is not null && WorkflowFindingRules.IsAgentFallback(state.Requirements))
        {
            blockers.Add("requirements agent LLM call failed");
        }
        else if (_acceptanceCriteriaOptions.Enabled
                 && !(state.RequirementsSpec?.HasAcceptanceCriteria ?? false))
        {
            blockers.Add("requirements output did not include acceptance criteria");
        }

        if (state.Architecture is not null && WorkflowFindingRules.IsAgentFallback(state.Architecture))
        {
            blockers.Add("architecture agent LLM call failed");
        }

        if (state.AppliedFiles.Count == 0)
        {
            blockers.Add("no implementation files were generated or applied");
        }

        if (state.Audit is not null && AuditorAgent.HasBlockingFindings(state.Audit))
        {
            blockers.Add("unresolved audit findings");
        }

        if (_acceptanceCriteriaOptions.Enabled && state.AcceptanceCriteria is not null)
        {
            AcceptanceCriteriaReport? report = state.AcceptanceCriteria.AcceptanceCriteriaReport;
            if ((report is not null && AcceptanceCriteriaGate.HasBlockingFailures(report))
                || WorkflowFindingRules.HasBlockingFindings(state.AcceptanceCriteria.Findings))
            {
                blockers.Add("acceptance criteria gate failed");
            }
        }

        return blockers;
    }

    private async Task FinalizeWorkflowAsync(
        WorkflowState state,
        CancellationToken cancellationToken)
    {
        List<string> prBlockers = CollectPullRequestBlockers(state);

        string artifactDir = await WorkflowArtifactWriter.WriteAsync(state);
        state.AddTimeline($"Artifacts written to {artifactDir}");

        if (prBlockers.Count == 0)
        {
            state.Stage = WorkflowStage.ReadyForPR;
            state.AddTimeline("Workflow ready for PR.");
            await _mcpAdapter.PublishStatusAsync("Workflow ready for PR.");
            await _mcpAdapter.PublishPullRequestAsync(state, cancellationToken);
        }
        else
        {
            state.Stage = WorkflowStage.Blocked;
            string summary = string.Join(" | ", prBlockers);
            state.PullRequestStatus = $"PR skipped: {summary}";
            state.AddTimeline($"Pull request skipped ({prBlockers.Count} blocker(s)): {summary}");
            await _mcpAdapter.PublishStatusAsync("Workflow finished with blockers; pull request skipped.");
        }

        if (!string.IsNullOrWhiteSpace(state.PullRequestStatus))
        {
            state.AddTimeline($"PR status: {state.PullRequestStatus}");
        }

        if (!string.IsNullOrWhiteSpace(state.PullRequestUrl))
        {
            state.AddTimeline($"PR url: {state.PullRequestUrl}");
        }

        if (prBlockers.Count == 0)
        {
            state.Stage = WorkflowStage.Done;
            state.AddTimeline("Workflow completed.");
            await _mcpAdapter.PublishStatusAsync("Workflow completed.");
        }
    }
}
