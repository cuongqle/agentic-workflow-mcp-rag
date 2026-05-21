sealed partial class WorkflowOrchestrator
{
    private async Task RunCompilationFixLoopAsync(
        WorkflowState state,
        Dictionary<string, AppliedFileChange> rollbackChanges,
        CancellationToken cancellationToken)
    {
        int attempt = 0;
        while (state.BuildValidation is not null
               && attempt < _maxCompilationFixAttempts
               && HasUnresolvedCompilationProblems(state))
        {
            attempt++;
            state.Stage = WorkflowStage.Integrating;

            PrepareRecoveryContext(state, "Compilation fix");

            state.AddTimeline($"Compilation fix attempt {attempt} started (LLM recovery).");
            await _mcpAdapter.PublishStatusAsync($"Compilation fix attempt {attempt} started.");

            state.Recovery = await _recoveryAgent.ExecuteAsync(state, cancellationToken);
            state.AddTimeline($"Compilation fix attempt {attempt} output generated.");

            var applyResult = await GeneratedFileApplier.ApplyAsync(state);
            if (applyResult.AppliedFiles.Count > 0)
            {
                state.AddTimeline($"Compilation fix applied files: {string.Join(", ", applyResult.AppliedFiles)}");
                RollbackTracker.CaptureRollbackChanges(rollbackChanges, applyResult.AppliedChanges);
            }
            else
            {
                state.AddTimeline("Compilation fix produced no applicable file changes.");
            }

            state.ComplianceIssues.RemoveAll(WorkflowFindingRules.IsApplyRejectionComplianceIssue);
            foreach (var rejected in applyResult.RejectedFiles)
            {
                string issue = WorkflowFindingRules.FormatApplyRejectionComplianceIssue(
                    rejected.RelativePath,
                    rejected.Reason);
                state.AddTimeline(issue);
                state.ComplianceIssues.Add(issue);
            }

            RecordNuGetPackageChanges(state);

            state.BuildValidation = await _buildValidationAgent.ExecuteAsync(state, cancellationToken);
            if (state.BuildValidation.Findings.Count == 0)
            {
                state.AddTimeline($"Build passed after compilation fix attempt {attempt}.");
                await _mcpAdapter.PublishStatusAsync($"Build passed after compilation fix attempt {attempt}.");
                break;
            }

            foreach (var finding in state.BuildValidation.Findings)
            {
                state.AddTimeline($"Build finding after compilation fix {attempt}: [{finding.Severity}] {finding.Message}");
            }
            await _mcpAdapter.PublishStatusAsync($"Build still failing after compilation fix attempt {attempt}.");
        }

        if (ShouldAttemptTestQuarantine(state))
        {
            await TryQuarantineTestArtifactsAsync(state, rollbackChanges, cancellationToken);
            ApplyTestFailureReleasePolicy(state);
        }
    }

    private static bool HasUnresolvedCompilationProblems(WorkflowState state)
    {
        bool hasBuildErrors = state.BuildValidation?.Findings.Any(f =>
            !f.Message.Contains("Build FAILED", StringComparison.OrdinalIgnoreCase)
            && !f.Message.Contains("Build failed", StringComparison.OrdinalIgnoreCase)) == true;

        return hasBuildErrors || WorkflowFindingRules.HasUnresolvedApplyRejections(state);
    }

}
