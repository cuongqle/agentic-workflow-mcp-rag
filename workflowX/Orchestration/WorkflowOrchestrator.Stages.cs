using workflowX.Configuration;
using workflowX.Infrastructure;

sealed partial class WorkflowOrchestrator
{
    private void SaveCheckpoint(WorkflowState state) =>
        WorkflowStateCheckpointStore.Save(state, _resumeOptions.CheckpointPath);

    private void LogSkippedStage(WorkflowState state, WorkflowStage stage) =>
        state.AddTimeline($"Skipped {stage} (resumed from checkpoint).");

    private void EnsureOnFeatureBranch(WorkflowState state)
    {
        string branch = _mcpAdapter.EnsureFeatureBranch(state);
        if (string.IsNullOrWhiteSpace(branch))
        {
            state.AddTimeline("Feature branch not created; work may remain on the default branch.");
            return;
        }

        state.AddTimeline($"Feature branch: {branch}");
    }

    private async Task RunRequirementsStageAsync(WorkflowState state, CancellationToken cancellationToken)
    {
        state.Stage = WorkflowStage.Requirements;
        state.AddTimeline("Requirements intake started.");
        var requirementsResult = await _requirementsAgent.ExecuteAsync(state, cancellationToken);
        if (WorkflowFindingRules.IsAgentFallback(requirementsResult))
        {
            state.Requirements = requirementsResult;
            state.AddTimeline("Requirements intake degraded: requirements agent LLM call failed.");
            await _mcpAdapter.PublishStatusAsync("Requirements intake degraded; continuing workflow.");
        }
        else
        {
            state.Requirements = requirementsResult;
            state.RequirementsSpec = requirementsResult.RequirementsSpec ?? RequirementsSpecParser.Resolve(requirementsResult);
            if (!state.RequirementsSpec.HasAcceptanceCriteria && _acceptanceCriteriaOptions.Enabled)
            {
                state.AddTimeline("Requirements issue: no acceptance criteria parsed.");
                await _mcpAdapter.PublishStatusAsync("Requirements missing acceptance criteria; continuing workflow.");
            }

            string requirementsArtifactDir = WorkflowArtifactWriter.WriteRequirementsArtifacts(state);
            state.AddTimeline(
                $"Requirements artifacts written to {requirementsArtifactDir} "
                + $"(requirements.md, requirements.json, {state.RequirementsSpec.AcceptanceCriteria.Count} acceptance criteria).");
            await _mcpAdapter.PublishStatusAsync("Requirements intake completed.");
        }

        state.Requirements ??= requirementsResult;
        state.RequirementsSpec ??= requirementsResult.RequirementsSpec ?? RequirementsSpecParser.Resolve(requirementsResult);
        WorkflowTaskNaming.RefineTaskFromRequirements(state);
        EnsureOnFeatureBranch(state);
        state.Stage = WorkflowStage.Planning;
        SaveCheckpoint(state);
    }

    private async Task RunPlanningStageAsync(WorkflowState state, CancellationToken cancellationToken)
    {
        EnsureOnFeatureBranch(state);
        state.Stage = WorkflowStage.Planning;
        state.AddTimeline("Architecture planning started.");
        var architectureResult = await _architectureAgent.ExecuteAsync(state, cancellationToken);
        if (WorkflowFindingRules.IsAgentFallback(architectureResult))
        {
            state.Architecture = architectureResult;
            state.AddTimeline("Architecture planning degraded: architecture agent LLM call failed.");
            await _mcpAdapter.PublishStatusAsync("Architecture planning degraded; continuing workflow.");
        }
        else
        {
            state.Architecture = WorkflowFindingRules.SanitizeArchitectureResult(architectureResult);
            state.ArchitecturePlan = architectureResult.ArchitecturePlan
                                     ?? ArchitecturePlanParser.ParseMarkdown(state.Architecture.Summary);
            if (state.ArchitecturePlan is not null
                && (state.ArchitecturePlan.HasBackendDeliverables || state.ArchitecturePlan.HasFrontendDeliverables))
            {
                state.AddTimeline(
                    $"Architecture plan parsed: backend={state.ArchitecturePlan.BackendFiles.Count} file(s), "
                    + $"frontend={state.ArchitecturePlan.FrontendFiles.Count} file(s).");
            }

            if (architectureResult.Summary.Contains("```", StringComparison.Ordinal))
            {
                state.AddTimeline("Architecture plan sanitized (removed sample code blocks before implementation).");
            }

            await _mcpAdapter.PublishStatusAsync("Architecture plan completed.");
            state.AddTimeline($"Repository layers (contract/RAG): {WorkflowFindingRules.FormatRepoCapabilities(state)}.");

            string architectureArtifactDir = WorkflowArtifactWriter.WriteArchitectureArtifacts(state);
            state.AddTimeline(
                $"Architecture artifacts written to {architectureArtifactDir} (architecture-plan.md, architecture-plan.json).");
        }

        state.Architecture ??= architectureResult;
        state.ArchitecturePlan ??= architectureResult.ArchitecturePlan
                                   ?? ArchitecturePlanParser.ParseMarkdown(state.Architecture?.Summary);
        state.Stage = WorkflowStage.Implementing;
        SaveCheckpoint(state);
    }

    private async Task RunImplementationStageAsync(
        WorkflowState state,
        Dictionary<string, string> pendingApplyRejections,
        Dictionary<string, AppliedFileChange> rollbackChanges,
        CancellationToken cancellationToken)
    {
        state.Stage = WorkflowStage.Implementing;
        state.AddTimeline("Implementation started.");
        EnsureOnFeatureBranch(state);
        ImplementationScopeDetails scopeDetails = WorkflowFindingRules.ResolveImplementationScopeDetails(state);
        (bool runBackend, bool runFrontend) = scopeDetails.Scope;
        state.AddTimeline(WorkflowFindingRules.FormatImplementationScopeDiagnostics(scopeDetails));

        Task<AgentResult> backendTask = runBackend
            ? _backendDeveloperAgent.ExecuteAsync(state, cancellationToken)
            : Task.FromResult(WorkflowFindingRules.SkippedAgentResult(
                "BackendDeveloperAgent",
                "Skipped: repository or architecture plan has no backend deliverables."));
        Task<AgentResult> frontendTask = runFrontend
            ? _frontendDeveloperAgent.ExecuteAsync(state, cancellationToken)
            : Task.FromResult(WorkflowFindingRules.SkippedAgentResult(
                "FrontendDeveloperAgent",
                "Skipped: repository or architecture plan has no frontend deliverables."));
        await Task.WhenAll(backendTask, frontendTask);
        state.Backend = await backendTask;
        state.Frontend = await frontendTask;

        if (runBackend && WorkflowFindingRules.IsAgentFallback(state.Backend))
        {
            state.AddTimeline($"Backend developer degraded: {state.Backend.Summary}");
        }

        if (runFrontend && WorkflowFindingRules.IsAgentFallback(state.Frontend))
        {
            state.AddTimeline($"Frontend developer degraded: {state.Frontend.Summary}");
        }

        if (!runBackend && !runFrontend)
        {
            await _mcpAdapter.PublishStatusAsync("Implementation skipped: architecture plan has no BACKEND_FILES or FRONTEND_FILES.");
        }
        else
        {
            await _mcpAdapter.PublishStatusAsync(
                $"Implementation completed (backend files: {state.Backend?.ProposedFiles.Count ?? 0}, "
                + $"frontend files: {state.Frontend?.ProposedFiles.Count ?? 0}).");
        }

        if (WorkflowFindingRules.CountProposedImplementationFiles(state) == 0)
        {
            string reason = WorkflowFindingRules.DescribeMissingImplementationReason(runBackend, runFrontend, state);
            state.AddTimeline($"No generated files: {reason}");
            await _mcpAdapter.PublishStatusAsync($"No files generated — {reason}");
        }

        var applyResult = await GeneratedFileApplier.ApplyAsync(state);
        WorkflowFindingRules.RecordApplyRejections(state, pendingApplyRejections, applyResult);
        state.AppliedFiles.AddRange(applyResult.AppliedFiles);
        RecordNuGetPackageChanges(state);
        if (applyResult.AppliedFiles.Count > 0)
        {
            state.AddTimeline($"Generated files applied: {string.Join(", ", applyResult.AppliedFiles)}");
            await _mcpAdapter.PublishStatusAsync($"Applied {applyResult.AppliedFiles.Count} generated file(s) to repository.");
            RollbackTracker.CaptureRollbackChanges(rollbackChanges, applyResult.AppliedChanges);
        }
        else if (WorkflowFindingRules.CountProposedImplementationFiles(state) > 0)
        {
            state.AddTimeline("Developer agents proposed files but none were applied (all rejected or invalid).");
            await _mcpAdapter.PublishStatusAsync("No files applied — all proposed files were rejected.");
        }
        else
        {
            state.AddTimeline("No generated source files were produced by developer agents.");
        }

        if (applyResult.RejectedFiles.Count > 0)
        {
            foreach (ApplyIssue rejected in applyResult.RejectedFiles)
            {
                state.AddTimeline(
                    $"Generation quality finding: [High] {WorkflowFindingRules.FormatApplyRejectionComplianceIssue(rejected.RelativePath, rejected.Reason)}");
            }

            await _mcpAdapter.PublishStatusAsync($"Rejected {applyResult.RejectedFiles.Count} low-quality generated file(s).");
        }

        List<AgentFinding> complianceFindings =
            WorkflowFindingRules.CollectComplianceFindings(state, pendingApplyRejections);
        foreach (var finding in complianceFindings)
        {
            state.AddTimeline($"Compliance finding: [{finding.Severity}] {finding.Message}");
            state.ComplianceIssues.Add($"[{finding.Severity}] {finding.Message}");
        }
        if (complianceFindings.Count > 0)
        {
            await _mcpAdapter.PublishStatusAsync($"Detected {complianceFindings.Count} contract compliance gap(s).");
        }
        if (WorkflowFindingRules.HasBlockingFindings(complianceFindings))
        {
            state.BuildValidation = new AgentResult
            {
                AgentName = "BuildValidationAgent",
                Summary = "Compilation fix triggered by blocking repository contract findings.",
                Findings = complianceFindings
                    .Where(f => f.Severity is FindingSeverity.High or FindingSeverity.Blocker)
                    .ToList()
            };
            await RunCompilationFixLoopAsync(state, rollbackChanges, pendingApplyRejections, cancellationToken);

            complianceFindings = WorkflowFindingRules.CollectComplianceFindings(state, pendingApplyRejections);
        }

        if (WorkflowFindingRules.CountProposedImplementationFiles(state) == 0
            && applyResult.AppliedFiles.Count == 0)
        {
            state.AddTimeline("Implementation issue: no files were generated or applied.");
            await _mcpAdapter.PublishStatusAsync("No files generated or applied; continuing workflow.");
        }

        state.Stage = WorkflowStage.Integrating;
        SaveCheckpoint(state);
    }

    private async Task RunIntegratingStageAsync(
        WorkflowState state,
        Dictionary<string, string> pendingApplyRejections,
        Dictionary<string, AppliedFileChange> rollbackChanges,
        CancellationToken cancellationToken)
    {
        state.Stage = WorkflowStage.Integrating;
        state.AddTimeline("Build validation started.");
        state.BuildValidation = await _buildValidationAgent.ExecuteAsync(state, cancellationToken);
        if (state.BuildValidation.Findings.Count > 0)
        {
            foreach (var finding in state.BuildValidation.Findings)
            {
                state.AddTimeline($"Build finding: [{finding.Severity}] {finding.Message}");
            }
            await _mcpAdapter.PublishStatusAsync($"Build validation found {state.BuildValidation.Findings.Count} issue(s).");

            await RunCompilationFixLoopAsync(state, rollbackChanges, pendingApplyRejections, cancellationToken);
        }
        else
        {
            state.AddTimeline("Build validation passed.");
            await _mcpAdapter.PublishStatusAsync("Build validation passed.");
        }

        state.AddTimeline("Integration observation started.");
        state.Observer = await _observerAgent.ExecuteAsync(state, cancellationToken);
        await _mcpAdapter.PublishStatusAsync("Observer review completed.");
        state.Stage = WorkflowStage.Auditing;
        SaveCheckpoint(state);
    }

    private async Task RunAuditingStageAsync(
        WorkflowState state,
        Dictionary<string, string> pendingApplyRejections,
        CancellationToken cancellationToken)
    {
        state.Stage = WorkflowStage.Auditing;
        state.AddTimeline("Audit started.");
        state.Audit = await _auditorAgent.ExecuteAsync(state, cancellationToken);
        TestReleasePolicySupport.RefreshComplianceAuditFindings(state, pendingApplyRejections);
        await _mcpAdapter.PublishStatusAsync("Audit completed.");
        SaveCheckpoint(state);
    }

    private async Task RunRecoveryLoopAsync(
        WorkflowState state,
        Dictionary<string, string> pendingApplyRejections,
        Dictionary<string, AppliedFileChange> rollbackChanges,
        CancellationToken cancellationToken,
        bool resumeIntoRecoveryLoop)
    {
        if (resumeIntoRecoveryLoop)
        {
            state.AddTimeline($"Resuming recovery loop at attempt {state.RecoveryAttemptCount + 1}.");
        }

        while ((resumeIntoRecoveryLoop || AuditorAgent.HasBlockingFindings(state.Audit))
               && state.RecoveryAttemptCount < _maxRecoveryAttempts)
        {
            resumeIntoRecoveryLoop = false;
            state.Stage = WorkflowStage.Recovering;
            state.RecoveryAttemptCount++;
            SaveCheckpoint(state);
            state.AddTimeline($"Recovery attempt {state.RecoveryAttemptCount} started.");
            PrepareRecoveryContext(state, "Recovery");
            state.Recovery = await _recoveryAgent.ExecuteAsync(state, cancellationToken);
            await _mcpAdapter.PublishStatusAsync($"Recovery attempt {state.RecoveryAttemptCount} completed.");

            var recoveredResult = await GeneratedFileApplier.ApplyAsync(state);
            WorkflowFindingRules.RecordApplyRejections(state, pendingApplyRejections, recoveredResult);
            state.AppliedFiles.AddRange(recoveredResult.AppliedFiles);
            RecordNuGetPackageChanges(state);
            if (recoveredResult.AppliedFiles.Count > 0)
            {
                state.AddTimeline($"Recovery files applied: {string.Join(", ", recoveredResult.AppliedFiles)}");
                await _mcpAdapter.PublishStatusAsync($"Applied {recoveredResult.AppliedFiles.Count} recovery file(s).");
                RollbackTracker.CaptureRollbackChanges(rollbackChanges, recoveredResult.AppliedChanges);
            }
            else
            {
                state.AddTimeline("Recovery produced no concrete file changes.");
            }
            if (recoveredResult.RejectedFiles.Count > 0)
            {
                foreach (ApplyIssue rejected in recoveredResult.RejectedFiles)
                {
                    state.AddTimeline(
                        $"Recovery quality finding: [High] {WorkflowFindingRules.FormatApplyRejectionComplianceIssue(rejected.RelativePath, rejected.Reason)}");
                }

                await _mcpAdapter.PublishStatusAsync($"Rejected {recoveredResult.RejectedFiles.Count} low-quality recovery file(s).");
            }

            state.BuildValidation = await _buildValidationAgent.ExecuteAsync(state, cancellationToken);
            if (state.BuildValidation.Findings.Count > 0)
            {
                foreach (var finding in state.BuildValidation.Findings)
                {
                    state.AddTimeline($"Build finding after recovery: [{finding.Severity}] {finding.Message}");
                }
                await _mcpAdapter.PublishStatusAsync($"Build still failing after recovery attempt {state.RecoveryAttemptCount}.");
            }
            else
            {
                state.AddTimeline($"Build passed after recovery attempt {state.RecoveryAttemptCount}.");
                await _mcpAdapter.PublishStatusAsync($"Build passed after recovery attempt {state.RecoveryAttemptCount}.");
            }

            if (WorkflowFindingRules.HasUnresolvedCompilationProblems(state))
            {
                await RunCompilationFixLoopAsync(state, rollbackChanges, pendingApplyRejections, cancellationToken);
            }

            state.Stage = WorkflowStage.Auditing;
            state.AddTimeline($"Re-audit after recovery attempt {state.RecoveryAttemptCount}.");
            state.Audit = await _auditorAgent.ExecuteAsync(state, cancellationToken);
            TestReleasePolicySupport.RefreshComplianceAuditFindings(state, pendingApplyRejections);
            SaveCheckpoint(state);
        }
    }

    private async Task RunAcceptanceStageAsync(WorkflowState state, CancellationToken cancellationToken)
    {
        state.Stage = WorkflowStage.ValidatingAcceptance;
        state.AddTimeline("Acceptance criteria gate started.");
        await _mcpAdapter.PublishStatusAsync("Validating acceptance criteria.");

        AcceptanceCriteriaReport deterministicReport =
            AcceptanceCriteriaGate.EvaluateDeterministic(state, _acceptanceCriteriaOptions);
        var acceptanceAgentResult = await _acceptanceCriteriaAgent.ExecuteAsync(state, cancellationToken);
        RequirementsSpec requirements = RequirementsSpecParser.ResolveForWorkflow(state);
        AcceptanceCriteriaReport mergedReport = AcceptanceCriteriaGate.MergeReports(
            deterministicReport,
            acceptanceAgentResult.AcceptanceCriteriaReport,
            requirements);
        var gateFindings = AcceptanceCriteriaGate.ToFindings(mergedReport).ToList();
        if (WorkflowFindingRules.IsAgentFallback(acceptanceAgentResult))
        {
            gateFindings.Add(new AgentFinding
            {
                Severity = FindingSeverity.High,
                Message = $"AcceptanceCriteriaAgent LLM call failed: {acceptanceAgentResult.Summary}"
            });
        }

        state.AcceptanceCriteria = new AgentResult
        {
            AgentName = "AcceptanceCriteriaGate",
            Summary = AcceptanceCriteriaReportParser.FormatReadableSummary(mergedReport),
            AcceptanceCriteriaReport = mergedReport,
            Findings = gateFindings
        };
        state.AddTimeline(AcceptanceCriteriaGate.FormatGateDiagnostics(mergedReport));
        foreach (AgentFinding finding in gateFindings)
        {
            state.AddTimeline($"Acceptance gate finding: [{finding.Severity}] {finding.Message}");
            state.ComplianceIssues.Add($"[{finding.Severity}] {finding.Message}");
        }

        string acceptanceArtifactDir = WorkflowArtifactWriter.WriteAcceptanceArtifacts(state);
        state.AddTimeline($"Acceptance artifacts written to {acceptanceArtifactDir}.");

        if (AcceptanceCriteriaGate.HasBlockingFailures(mergedReport)
            || WorkflowFindingRules.HasBlockingFindings(gateFindings))
        {
            state.AddTimeline("Acceptance criteria issue: definition of done not satisfied.");
            await _mcpAdapter.PublishStatusAsync("Acceptance criteria gate failed; continuing to finalize workflow.");
        }
        else
        {
            await _mcpAdapter.PublishStatusAsync("Acceptance criteria gate passed.");
        }

        SaveCheckpoint(state);
    }
}
