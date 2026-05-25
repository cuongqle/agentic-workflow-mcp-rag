using workflowX.Infrastructure;

sealed partial class WorkflowOrchestrator
{
    private async Task RunCompilationFixLoopAsync(
        WorkflowState state,
        Dictionary<string, AppliedFileChange> rollbackChanges,
        Dictionary<string, string> pendingApplyRejections,
        CancellationToken cancellationToken)
    {
        int attempt = 0;
        while (state.BuildValidation is not null
               && attempt < _maxCompilationFixAttempts
               && WorkflowFindingRules.HasUnresolvedCompilationProblems(state))
        {
            attempt++;
            state.Stage = WorkflowStage.Integrating;

            PrepareRecoveryContext(state, "Compilation fix");

            state.AddTimeline($"Compilation fix attempt {attempt} started (LLM recovery).");
            await _mcpAdapter.PublishStatusAsync($"Compilation fix attempt {attempt} started.");

            state.Recovery = await _recoveryAgent.ExecuteAsync(state, cancellationToken);
            state.AddTimeline($"Compilation fix attempt {attempt} output generated.");

            var applyResult = await GeneratedFileApplier.ApplyAsync(state);
            WorkflowFindingRules.RecordApplyRejections(state, pendingApplyRejections, applyResult);
            state.AppliedFiles.AddRange(applyResult.AppliedFiles);
            if (applyResult.AppliedFiles.Count > 0)
            {
                state.AddTimeline($"Compilation fix applied files: {string.Join(", ", applyResult.AppliedFiles)}");
                RollbackTracker.CaptureRollbackChanges(rollbackChanges, applyResult.AppliedChanges);
            }
            else
            {
                state.AddTimeline("Compilation fix produced no applicable file changes.");
            }

            foreach (ApplyIssue rejected in applyResult.RejectedFiles)
            {
                state.AddTimeline(
                    WorkflowFindingRules.FormatApplyRejectionComplianceIssue(rejected.RelativePath, rejected.Reason));
            }

            RecordNuGetPackageChanges(state);

            state.BuildValidation = await _buildValidationAgent.ExecuteAsync(state, cancellationToken);
            if (!WorkflowFindingRules.HasActionableBuildFindings(state))
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

        if (TestReleasePolicySupport.ShouldAttemptQuarantine(state))
        {
            await TestReleasePolicySupport.TryQuarantineAsync(
                state,
                rollbackChanges,
                _buildValidationAgent.ExecuteAsync,
                _mcpAdapter.PublishStatusAsync,
                cancellationToken);
            TestReleasePolicySupport.ApplyReleasePolicy(state);
        }
    }

    private void PrepareRecoveryContext(WorkflowState state, string timelineLabel) =>
        RecoveryContextSupport.Prepare(
            state,
            timelineLabel,
            _compilationFixContextOptions,
            state.AddTimeline);

    private static void RecordNuGetPackageChanges(WorkflowState state)
    {
        if (state.Contract is { Stack.DotNet: false })
        {
            return;
        }

        foreach (string packageChange in ProjectPackageAuditor.EnsureMissingPackages(
                     state.RepoPath,
                     state.BuildValidation?.Findings,
                     WorkflowFindingRules.GetAllProposedFiles(state)))
        {
            state.AddTimeline($"NuGet restore: {packageChange}");
        }
    }
}
