using System.Text.RegularExpressions;
using agents_mcp_rag.Configuration;
using agents_mcp_rag.Infrastructure;

sealed partial class WorkflowOrchestrator
{
    private readonly ArchitectureAgent _architectureAgent;
    private readonly ObserverAgent _observerAgent;
    private readonly BackendDeveloperAgent _backendDeveloperAgent;
    private readonly FrontendDeveloperAgent _frontendDeveloperAgent;
    private readonly BuildValidationAgent _buildValidationAgent;
    private readonly AuditorAgent _auditorAgent;
    private readonly RecoveryAgent _recoveryAgent;
    private readonly GitHubMcpAdapter _mcpAdapter;
    private readonly int _maxRecoveryAttempts;
    private readonly int _maxCompilationFixAttempts;
    private readonly CompilationFixContextOptions _compilationFixContextOptions;

    public WorkflowOrchestrator(
        ArchitectureAgent architectureAgent,
        ObserverAgent observerAgent,
        BackendDeveloperAgent backendDeveloperAgent,
        FrontendDeveloperAgent frontendDeveloperAgent,
        BuildValidationAgent buildValidationAgent,
        AuditorAgent auditorAgent,
        RecoveryAgent recoveryAgent,
        GitHubMcpAdapter mcpAdapter,
        int maxRecoveryAttempts,
        int maxCompilationFixAttempts,
        CompilationFixContextOptions? compilationFixContextOptions = null)
    {
        _architectureAgent = architectureAgent;
        _observerAgent = observerAgent;
        _backendDeveloperAgent = backendDeveloperAgent;
        _frontendDeveloperAgent = frontendDeveloperAgent;
        _buildValidationAgent = buildValidationAgent;
        _auditorAgent = auditorAgent;
        _recoveryAgent = recoveryAgent;
        _mcpAdapter = mcpAdapter;
        _maxRecoveryAttempts = Math.Max(1, maxRecoveryAttempts);
        _maxCompilationFixAttempts = Math.Max(1, maxCompilationFixAttempts);
        _compilationFixContextOptions = compilationFixContextOptions ?? new CompilationFixContextOptions();
    }

    public async Task<WorkflowState> RunAsync(WorkflowState state, CancellationToken cancellationToken = default)
    {
        state.AddTimeline("Workflow queued.");
        await _mcpAdapter.PublishStatusAsync($"Queued: {state.Task.Title}");

        state.Stage = WorkflowStage.Planning;
        state.AddTimeline("Architecture planning started.");
        var architectureResult = await _architectureAgent.ExecuteAsync(state, cancellationToken);
        if (WorkflowFindingRules.IsAgentFallback(architectureResult))
        {
            state.Architecture = architectureResult;
            state.Stage = WorkflowStage.Blocked;
            state.AddTimeline("Workflow blocked: architecture agent LLM call failed.");
            await _mcpAdapter.PublishStatusAsync("Workflow blocked: architecture planning failed.");
            return state;
        }

        state.Architecture = WorkflowFindingRules.SanitizeArchitectureResult(architectureResult);
        if (architectureResult.Summary.Contains("```", StringComparison.Ordinal))
        {
            state.AddTimeline("Architecture plan sanitized (removed sample code blocks before implementation).");
        }

        await _mcpAdapter.PublishStatusAsync("Architecture plan completed.");
        state.AddTimeline($"Repository layers (contract/RAG): {WorkflowFindingRules.FormatRepoCapabilities(state)}.");

        state.Stage = WorkflowStage.Implementing;
        state.AddTimeline("Implementation started.");
        var llmOutputQualityFindings = new List<AgentFinding>();
        var rollbackChanges = new Dictionary<string, AppliedFileChange>(StringComparer.OrdinalIgnoreCase);

        (bool runBackend, bool runFrontend) = WorkflowFindingRules.ResolveImplementationScope(state);
        state.AddTimeline($"Implementation scope: backend={runBackend}, frontend={runFrontend}.");

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
            llmOutputQualityFindings.Add(new AgentFinding
            {
                Severity = FindingSeverity.High,
                Message = $"BackendDeveloperAgent LLM call failed: {state.Backend.Summary}"
            });
        }

        if (runFrontend && WorkflowFindingRules.IsAgentFallback(state.Frontend))
        {
            llmOutputQualityFindings.Add(new AgentFinding
            {
                Severity = FindingSeverity.High,
                Message = $"FrontendDeveloperAgent LLM call failed: {state.Frontend.Summary}"
            });
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
            llmOutputQualityFindings.Add(new AgentFinding
            {
                Severity = FindingSeverity.High,
                Message = reason
            });
            state.AddTimeline($"No generated files: {reason}");
            await _mcpAdapter.PublishStatusAsync($"No files generated — {reason}");
        }

        var applyResult = await GeneratedFileApplier.ApplyAsync(state);
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
            foreach (var rejected in applyResult.RejectedFiles)
            {
                var finding = new AgentFinding
                {
                    Severity = FindingSeverity.High,
                    Message = WorkflowFindingRules.FormatApplyRejectionComplianceIssue(
                        rejected.RelativePath,
                        rejected.Reason)
                };
                llmOutputQualityFindings.Add(finding);
                state.AddTimeline($"Generation quality finding: [High] {finding.Message}");
                state.ComplianceIssues.Add(finding.Message);
            }
            await _mcpAdapter.PublishStatusAsync($"Rejected {applyResult.RejectedFiles.Count} low-quality generated file(s).");
        }

        var complianceFindings = ContractComplianceValidator.CollectComplianceFindings(state);
        complianceFindings.AddRange(llmOutputQualityFindings);
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
            await RunCompilationFixLoopAsync(state, rollbackChanges, cancellationToken);

            complianceFindings = ContractComplianceValidator.CollectComplianceFindings(state);
            complianceFindings.AddRange(llmOutputQualityFindings);
        }

        if (WorkflowFindingRules.CountProposedImplementationFiles(state) == 0
            && applyResult.AppliedFiles.Count == 0)
        {
            state.Stage = WorkflowStage.Blocked;
            state.AddTimeline("Workflow blocked: no implementation files were generated or applied.");
            await _mcpAdapter.PublishStatusAsync("Workflow blocked: no files generated.");
            return state;
        }

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

            await RunCompilationFixLoopAsync(state, rollbackChanges, cancellationToken);
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
        state.AddTimeline("Audit started.");
        state.Audit = await _auditorAgent.ExecuteAsync(state, cancellationToken);
        state.Audit.Findings.AddRange(complianceFindings);
        if (state.BuildValidation is not null)
        {
            state.Audit.Findings.AddRange(state.BuildValidation.Findings);
        }
        await _mcpAdapter.PublishStatusAsync("Audit completed.");

        while (AuditorAgent.HasBlockingFindings(state.Audit) && state.RecoveryAttemptCount < _maxRecoveryAttempts)
        {
            state.Stage = WorkflowStage.Recovering;
            state.RecoveryAttemptCount++;
            state.AddTimeline($"Recovery attempt {state.RecoveryAttemptCount} started.");
            PrepareRecoveryContext(state, "Recovery");
            state.Recovery = await _recoveryAgent.ExecuteAsync(state, cancellationToken);
            await _mcpAdapter.PublishStatusAsync($"Recovery attempt {state.RecoveryAttemptCount} completed.");

            var recoveredResult = await GeneratedFileApplier.ApplyAsync(state);
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
                foreach (var rejected in recoveredResult.RejectedFiles)
                {
                    var finding = new AgentFinding
                    {
                        Severity = FindingSeverity.High,
                        Message = WorkflowFindingRules.FormatApplyRejectionComplianceIssue(
                            rejected.RelativePath,
                            rejected.Reason)
                    };
                    llmOutputQualityFindings.Add(finding);
                    state.AddTimeline($"Recovery quality finding: [High] {finding.Message}");
                    state.ComplianceIssues.Add(finding.Message);
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

            if (state.BuildValidation.Findings.Count > 0
                || WorkflowFindingRules.HasUnresolvedApplyRejections(state))
            {
                await RunCompilationFixLoopAsync(state, rollbackChanges, cancellationToken);
            }

            state.Stage = WorkflowStage.Auditing;
            state.AddTimeline($"Re-audit after recovery attempt {state.RecoveryAttemptCount}.");
            state.Audit = await _auditorAgent.ExecuteAsync(state, cancellationToken);
            var postRecoveryCompliance = ContractComplianceValidator.CollectComplianceFindings(state);
            postRecoveryCompliance.AddRange(llmOutputQualityFindings);
            foreach (var finding in postRecoveryCompliance)
            {
                state.ComplianceIssues.Add($"[{finding.Severity}] {finding.Message}");
            }
            state.Audit.Findings.AddRange(postRecoveryCompliance);
            state.Audit.Findings.AddRange(state.BuildValidation.Findings);
        }

        ApplyTestFailureReleasePolicy(state);

        if (ShouldAttemptTestQuarantine(state))
        {
            await TryQuarantineTestArtifactsAsync(state, rollbackChanges, cancellationToken);
            RefreshAutomatedComplianceAuditFindings(state, llmOutputQualityFindings);
        }

        if (AuditorAgent.HasBlockingFindings(state.Audit))
        {
            bool productionBuildPassed = state.BuildValidation?.ProductionBuildPassed == true;
            if (!productionBuildPassed && state.BuildValidation is not null && state.BuildValidation.Findings.Count > 0 && rollbackChanges.Count > 0)
            {
                await GeneratedFileApplier.RollbackAsync(state.RepoPath, rollbackChanges.Values.ToList());
                state.AddTimeline("Rolled back generated changes because production compilation remained failing.");
                await _mcpAdapter.PublishStatusAsync("Rolled back generated changes due to unresolved production build failures.");
            }
            else if (productionBuildPassed)
            {
                state.AddTimeline("Production build passed; unresolved test-project issues were quarantined or downgraded.");
                await _mcpAdapter.PublishStatusAsync("Proceeding with production changes despite unresolved test-project issues.");
            }

            if (AuditorAgent.HasBlockingFindings(state.Audit))
            {
                state.Stage = WorkflowStage.Blocked;
                state.AddTimeline("Workflow blocked by unresolved audit findings.");
                await _mcpAdapter.PublishStatusAsync("Workflow blocked.");
                return state;
            }
        }

        state.Stage = WorkflowStage.ReadyForPR;
        state.AddTimeline("Workflow ready for PR.");
        await _mcpAdapter.PublishStatusAsync("Workflow ready for PR.");
        string artifactDir = await WorkflowArtifactWriter.WriteAsync(state);
        state.AddTimeline($"Artifacts written to {artifactDir}");
        await _mcpAdapter.PublishPullRequestAsync(state, cancellationToken);
        if (!string.IsNullOrWhiteSpace(state.PullRequestStatus))
        {
            state.AddTimeline($"PR status: {state.PullRequestStatus}");
        }
        if (!string.IsNullOrWhiteSpace(state.PullRequestUrl))
        {
            state.AddTimeline($"PR url: {state.PullRequestUrl}");
        }

        state.Stage = WorkflowStage.Done;
        state.AddTimeline("Workflow completed.");
        await _mcpAdapter.PublishStatusAsync("Workflow completed.");
        return state;
    }
}
