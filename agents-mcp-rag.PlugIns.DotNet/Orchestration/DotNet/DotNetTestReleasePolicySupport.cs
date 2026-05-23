using agents_mcp_rag.Infrastructure;

/// <summary>
/// DotNet test quarantine and release policy — production passes but test projects fail.
/// </summary>
static class DotNetTestReleasePolicySupport
{
    public static bool ShouldAttemptQuarantine(WorkflowState state) =>
        state.BuildValidation is not null
        && state.BuildValidation.ProductionBuildPassed == true
        && state.BuildValidation.Findings.Count > 0
        && BuildFailureClassifier.IsOnlyTestFailures(state.BuildValidation.Findings);

    public static async Task<bool> TryQuarantineAsync(
        WorkflowState state,
        Dictionary<string, AppliedFileChange> rollbackChanges,
        Func<WorkflowState, CancellationToken, Task<AgentResult>> revalidateBuildAsync,
        Func<string, Task> publishStatusAsync,
        CancellationToken cancellationToken)
    {
        var testChanges = rollbackChanges.Values
            .Where(change => BuildFailureClassifier.IsTestArtifactPath(change.RelativePath))
            .ToList();
        if (testChanges.Count == 0)
        {
            return false;
        }

        await ApplyRollback.RollbackAsync(state.RepoPath, testChanges);
        foreach (var change in testChanges)
        {
            rollbackChanges.Remove(change.RelativePath);
            RecordDeferredTestEntity(state, change.RelativePath);
        }

        state.AddTimeline($"Quarantined {testChanges.Count} test artifact(s) with unresolved compile errors.");
        await publishStatusAsync($"Quarantined {testChanges.Count} failing test artifact(s); production code retained.");

        state.BuildValidation = await revalidateBuildAsync(state, cancellationToken);
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

    public static void ApplyReleasePolicy(WorkflowState state)
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

    private static void RecordDeferredTestEntity(WorkflowState state, string relativePath)
    {
        string fileName = Path.GetFileName(relativePath);
        if (!fileName.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

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
