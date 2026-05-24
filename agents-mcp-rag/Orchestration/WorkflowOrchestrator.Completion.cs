sealed partial class WorkflowOrchestrator
{
    private static void BlockPullRequest(List<string> prBlockers, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return;
        }

        prBlockers.Add(reason);
    }

    private async Task FinalizeWorkflowAsync(
        WorkflowState state,
        List<string> prBlockers,
        CancellationToken cancellationToken)
    {
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
