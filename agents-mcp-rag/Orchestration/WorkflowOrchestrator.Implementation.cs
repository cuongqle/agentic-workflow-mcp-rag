using agents_mcp_rag.Infrastructure.Compliance.DotNet;

sealed partial class WorkflowOrchestrator
{
    private async Task TryApplySynthesizedMissingTestsAsync(
        WorkflowState state,
        Dictionary<string, AppliedFileChange> rollbackChanges)
    {
        if (!RepoStack.From(state).DotNet)
        {
            return;
        }

        IReadOnlyList<GeneratedFile> synthesized = MissingLayerTestSynthesizer.SynthesizeMissingTests(state);
        if (synthesized.Count == 0)
        {
            return;
        }

        state.Backend ??= new AgentResult { AgentName = "BackendDeveloperAgent", Summary = string.Empty };
        foreach (GeneratedFile file in synthesized)
        {
            state.Backend.ProposedFiles.RemoveAll(existing =>
                existing.RelativePath.Equals(file.RelativePath, StringComparison.OrdinalIgnoreCase));
            state.Backend.ProposedFiles.Add(file);
        }

        state.AddTimeline(
            $"Synthesized {synthesized.Count} missing unit test file(s): "
            + string.Join(", ", synthesized.Select(file => file.RelativePath)));
        await _mcpAdapter.PublishStatusAsync($"Synthesized {synthesized.Count} missing unit test file(s).");

        ApplyResult synthApply = await GeneratedFileApplier.ApplyAsync(state);
        state.AppliedFiles.AddRange(synthApply.AppliedFiles);
        RecordNuGetPackageChanges(state);
        if (synthApply.AppliedFiles.Count > 0)
        {
            state.AddTimeline($"Synthesized test files applied: {string.Join(", ", synthApply.AppliedFiles)}");
            await _mcpAdapter.PublishStatusAsync($"Applied {synthApply.AppliedFiles.Count} synthesized test file(s).");
            RollbackTracker.CaptureRollbackChanges(rollbackChanges, synthApply.AppliedChanges);
        }

        if (synthApply.RejectedFiles.Count > 0)
        {
            foreach (ApplyIssue rejected in synthApply.RejectedFiles)
            {
                state.AddTimeline(
                    $"Synthesized test rejected: {WorkflowFindingRules.FormatApplyRejectionComplianceIssue(rejected.RelativePath, rejected.Reason)}");
            }

            await _mcpAdapter.PublishStatusAsync($"Rejected {synthApply.RejectedFiles.Count} synthesized test file(s).");
        }
    }
}
