using agents_mcp_rag.Infrastructure;

sealed partial class WorkflowOrchestrator
{
    private static bool ShouldAttemptTestQuarantine(WorkflowState state)
    {
        return state.BuildValidation is not null
               && state.BuildValidation.ProductionBuildPassed == true
               && state.BuildValidation.Findings.Count > 0
               && BuildFailureClassifier.IsOnlyTestFailures(state.BuildValidation.Findings);
    }

    private async Task<bool> TryQuarantineTestArtifactsAsync(
        WorkflowState state,
        Dictionary<string, AppliedFileChange> rollbackChanges,
        CancellationToken cancellationToken)
    {
        var testChanges = rollbackChanges.Values
            .Where(change => BuildFailureClassifier.IsTestArtifactPath(change.RelativePath))
            .ToList();
        if (testChanges.Count == 0)
        {
            return false;
        }

        await GeneratedFileApplier.RollbackAsync(state.RepoPath, testChanges);
        foreach (var change in testChanges)
        {
            rollbackChanges.Remove(change.RelativePath);
            string fileName = Path.GetFileName(change.RelativePath);
            if (fileName.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase))
            {
                string subjectBaseName = Path.GetFileNameWithoutExtension(fileName);
                if (subjectBaseName.EndsWith("Tests", StringComparison.OrdinalIgnoreCase))
                {
                    subjectBaseName = subjectBaseName[..^"Tests".Length];
                }

                if (!string.IsNullOrWhiteSpace(subjectBaseName))
                {
                    state.DeferredTestEntities.Add(subjectBaseName);
                }
            }
        }

        state.AddTimeline($"Quarantined {testChanges.Count} test artifact(s) with unresolved compile errors.");
        await _mcpAdapter.PublishStatusAsync($"Quarantined {testChanges.Count} failing test artifact(s); production code retained.");

        state.BuildValidation = await _buildValidationAgent.ExecuteAsync(state, cancellationToken);
        if (state.BuildValidation.Findings.Count > 0)
        {
            foreach (var finding in state.BuildValidation.Findings)
            {
                state.AddTimeline($"Build finding after test quarantine: [{finding.Severity}] {finding.Message}");
            }
        }
        else
        {
            state.AddTimeline("Build passed after quarantining failing test artifacts.");
        }

        return true;
    }

    private static void RefreshAutomatedComplianceAuditFindings(
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

        ApplyTestFailureReleasePolicy(state);
    }

    private static void ApplyTestFailureReleasePolicy(WorkflowState state)
    {
        if (state.Audit is null || state.BuildValidation?.ProductionBuildPassed != true)
        {
            return;
        }

        var analysis = BuildFailureClassifier.Analyze(state.BuildValidation.Findings);
        if (!analysis.IsTestOnly && !analysis.HasTestFailures)
        {
            return;
        }

        var downgradedMessages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var finding in state.Audit.Findings.ToList())
        {
            if (finding.Severity is not (FindingSeverity.High or FindingSeverity.Blocker))
            {
                continue;
            }

            if (BuildFailureClassifier.ClassifyMessage(finding.Message) != BuildFailureScope.Test
                && !finding.Message.Contains("Missing unit test", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            state.Audit.Findings.Remove(finding);
            if (downgradedMessages.Add(finding.Message))
            {
                state.Audit.Findings.Add(new AgentFinding
                {
                    Severity = FindingSeverity.Medium,
                    Message = $"Deferred test gate: {finding.Message}"
                });
            }
        }

        foreach (string entity in state.DeferredTestEntities)
        {
            string deferredMessage = $"Deferred unit test generation for {entity} repository after repeated test compile failures.";
            if (downgradedMessages.Add(deferredMessage))
            {
                state.Audit.Findings.Add(new AgentFinding
                {
                    Severity = FindingSeverity.Medium,
                    Message = deferredMessage
                });
            }
        }
    }
}
