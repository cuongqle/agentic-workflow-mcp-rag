using agents_mcp_rag.Infrastructure;

sealed partial class WorkflowOrchestrator
{
    private async Task RunCompilationFixLoopAsync(
        WorkflowState state,
        Dictionary<string, AppliedFileChange> rollbackChanges,
        CancellationToken cancellationToken)
    {
        int attempt = 0;
        while (state.BuildValidation is not null
               && state.BuildValidation.Findings.Count > 0
               && attempt < _maxCompilationFixAttempts)
        {
            attempt++;
            state.Stage = WorkflowStage.Integrating;
            state.CompilationFixAllowedFiles = CompilationFixFileResolver.DetermineAllowedFiles(state);
            state.CompilationContractContext = CompilationContractContextBuilder.Build(
                state.RepoPath,
                state.CompilationFixAllowedFiles,
                state.BuildValidation?.Findings);
            state.AddTimeline($"Compilation fix attempt {attempt} started.");
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

            foreach (var rejected in applyResult.RejectedFiles)
            {
                state.AddTimeline($"Compilation fix rejected '{rejected.RelativePath}': {rejected.Reason}");
                state.ComplianceIssues.Add($"Compilation fix rejected '{rejected.RelativePath}': {rejected.Reason}");
            }

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
}
