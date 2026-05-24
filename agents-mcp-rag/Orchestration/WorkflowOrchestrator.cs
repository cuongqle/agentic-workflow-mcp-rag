using System.Text.RegularExpressions;
using agents_mcp_rag.Configuration;
using agents_mcp_rag.Infrastructure;

sealed partial class WorkflowOrchestrator
{
    private readonly RequirementsAgent _requirementsAgent;
    private readonly ArchitectureAgent _architectureAgent;
    private readonly ObserverAgent _observerAgent;
    private readonly BackendDeveloperAgent _backendDeveloperAgent;
    private readonly FrontendDeveloperAgent _frontendDeveloperAgent;
    private readonly BuildValidationAgent _buildValidationAgent;
    private readonly AuditorAgent _auditorAgent;
    private readonly RecoveryAgent _recoveryAgent;
    private readonly AcceptanceCriteriaAgent _acceptanceCriteriaAgent;
    private readonly GitHubMcpAdapter _mcpAdapter;
    private readonly int _maxRecoveryAttempts;
    private readonly int _maxCompilationFixAttempts;
    private readonly CompilationFixContextOptions _compilationFixContextOptions;
    private readonly AcceptanceCriteriaOptions _acceptanceCriteriaOptions;

    public WorkflowOrchestrator(
        RequirementsAgent requirementsAgent,
        ArchitectureAgent architectureAgent,
        ObserverAgent observerAgent,
        BackendDeveloperAgent backendDeveloperAgent,
        FrontendDeveloperAgent frontendDeveloperAgent,
        BuildValidationAgent buildValidationAgent,
        AuditorAgent auditorAgent,
        RecoveryAgent recoveryAgent,
        AcceptanceCriteriaAgent acceptanceCriteriaAgent,
        GitHubMcpAdapter mcpAdapter,
        int maxRecoveryAttempts,
        int maxCompilationFixAttempts,
        CompilationFixContextOptions? compilationFixContextOptions = null,
        AcceptanceCriteriaOptions? acceptanceCriteriaOptions = null)
    {
        _requirementsAgent = requirementsAgent;
        _architectureAgent = architectureAgent;
        _observerAgent = observerAgent;
        _backendDeveloperAgent = backendDeveloperAgent;
        _frontendDeveloperAgent = frontendDeveloperAgent;
        _buildValidationAgent = buildValidationAgent;
        _auditorAgent = auditorAgent;
        _recoveryAgent = recoveryAgent;
        _acceptanceCriteriaAgent = acceptanceCriteriaAgent;
        _mcpAdapter = mcpAdapter;
        _maxRecoveryAttempts = Math.Max(1, maxRecoveryAttempts);
        _maxCompilationFixAttempts = Math.Max(1, maxCompilationFixAttempts);
        _compilationFixContextOptions = compilationFixContextOptions ?? new CompilationFixContextOptions();
        _acceptanceCriteriaOptions = acceptanceCriteriaOptions ?? new AcceptanceCriteriaOptions();
    }

    public async Task<WorkflowState> RunAsync(WorkflowState state, CancellationToken cancellationToken = default)
    {
        state.AddTimeline("Workflow queued.");
        await _mcpAdapter.PublishStatusAsync($"Queued: {state.Task.Title}");

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
        state.AddTimeline("Implementation started.");
        var llmOutputQualityFindings = new List<AgentFinding>();
        var rollbackChanges = new Dictionary<string, AppliedFileChange>(StringComparer.OrdinalIgnoreCase);

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

        await TryApplySynthesizedMissingTestsAsync(state, rollbackChanges);

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
            state.AddTimeline("Implementation issue: no files were generated or applied.");
            await _mcpAdapter.PublishStatusAsync("No files generated or applied; continuing workflow.");
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

            if (WorkflowFindingRules.HasUnresolvedCompilationProblems(state))
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

        TestReleasePolicySupport.ApplyReleasePolicy(state);

        if (TestReleasePolicySupport.ShouldAttemptQuarantine(state))
        {
            await TestReleasePolicySupport.TryQuarantineAsync(
                state,
                rollbackChanges,
                _buildValidationAgent.ExecuteAsync,
                _mcpAdapter.PublishStatusAsync,
                cancellationToken);
            TestReleasePolicySupport.RefreshComplianceAuditFindings(state, llmOutputQualityFindings);
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
                state.AddTimeline("Audit issue: unresolved blocking findings remain.");
                await _mcpAdapter.PublishStatusAsync("Audit has blocking findings; continuing workflow.");
            }
        }

        if (_acceptanceCriteriaOptions.Enabled)
        {
            state.Stage = WorkflowStage.ValidatingAcceptance;
            state.AddTimeline("Acceptance criteria gate started.");
            await _mcpAdapter.PublishStatusAsync("Validating acceptance criteria.");

            AcceptanceCriteriaReport deterministicReport =
                AcceptanceCriteriaGate.EvaluateDeterministic(state, _acceptanceCriteriaOptions);
            var acceptanceAgentResult = await _acceptanceCriteriaAgent.ExecuteAsync(state, cancellationToken);
            RequirementsSpec requirements = state.RequirementsSpec ?? new RequirementsSpec();
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
        }

        await FinalizeWorkflowAsync(state, cancellationToken);
        return state;
    }
}
