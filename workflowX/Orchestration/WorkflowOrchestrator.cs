using workflowX.Configuration;
using workflowX.Infrastructure;

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
    private WorkflowResumeOptions _resumeOptions = new();

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

    public Task<WorkflowState> RunAsync(WorkflowState state, CancellationToken cancellationToken = default) =>
        RunAsync(state, new WorkflowResumeOptions(), cancellationToken);

    public async Task<WorkflowState> RunAsync(
        WorkflowState state,
        WorkflowResumeOptions resumeOptions,
        CancellationToken cancellationToken = default)
    {
        _resumeOptions = resumeOptions;
        WorkflowStage startFrom = resumeOptions.ResolveStartStage(state);
        bool resumeIntoRecoveryLoop = startFrom == WorkflowStage.Recovering && state.Audit is not null;

        state.AddTimeline("Workflow queued.");
        await _mcpAdapter.PublishStatusAsync($"Queued: {state.Task.Title}");

        if (startFrom != WorkflowStage.Requirements && startFrom != WorkflowStage.Queued)
        {
            state.AddTimeline($"Resuming workflow from {startFrom}.");
            await _mcpAdapter.PublishStatusAsync($"Resuming workflow from {startFrom}.");
        }

        var pendingApplyRejections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var rollbackChanges = new Dictionary<string, AppliedFileChange>(StringComparer.OrdinalIgnoreCase);

        if (WorkflowStageResume.ShouldRun(startFrom, WorkflowStage.Requirements))
        {
            await RunRequirementsStageAsync(state, cancellationToken);
        }
        else
        {
            LogSkippedStage(state, WorkflowStage.Requirements);
        }

        if (WorkflowStageResume.ShouldRun(startFrom, WorkflowStage.Planning))
        {
            await RunPlanningStageAsync(state, cancellationToken);
        }
        else
        {
            LogSkippedStage(state, WorkflowStage.Planning);
        }

        if (WorkflowStageResume.ShouldRun(startFrom, WorkflowStage.Implementing))
        {
            await RunImplementationStageAsync(state, pendingApplyRejections, rollbackChanges, cancellationToken);
        }
        else
        {
            LogSkippedStage(state, WorkflowStage.Implementing);
        }

        if (WorkflowStageResume.ShouldRun(startFrom, WorkflowStage.Integrating))
        {
            await RunIntegratingStageAsync(state, pendingApplyRejections, rollbackChanges, cancellationToken);
        }
        else if (!resumeIntoRecoveryLoop)
        {
            LogSkippedStage(state, WorkflowStage.Integrating);
        }

        if (WorkflowStageResume.ShouldRun(startFrom, WorkflowStage.Auditing) && !resumeIntoRecoveryLoop)
        {
            await RunAuditingStageAsync(state, pendingApplyRejections, cancellationToken);
        }
        else if (!resumeIntoRecoveryLoop)
        {
            LogSkippedStage(state, WorkflowStage.Auditing);
        }

        if (WorkflowStageResume.ShouldEnterRecoveryLoop(startFrom, state)
            || (resumeIntoRecoveryLoop && AuditorAgent.HasBlockingFindings(state.Audit)))
        {
            await RunRecoveryLoopAsync(
                state,
                pendingApplyRejections,
                rollbackChanges,
                cancellationToken,
                resumeIntoRecoveryLoop);
        }

        TestReleasePolicySupport.ApplyReleasePolicy(state);

        TestReleasePolicySupport.RefreshComplianceAuditFindings(state, pendingApplyRejections);

        if (AuditorAgent.HasBlockingFindings(state.Audit))
        {
            bool productionBuildPassed = state.BuildValidation?.ProductionBuildPassed == true;
            if (!productionBuildPassed
                && state.BuildValidation is not null
                && state.BuildValidation.Findings.Count > 0
                && rollbackChanges.Count > 0)
            {
                state.AddTimeline(
                    "Production build still failing; applied changes preserved on disk for human review or resume (--from Recovering).");
                await _mcpAdapter.PublishStatusAsync("Applied changes preserved; fix locally or resume workflow.");
            }
            else if (productionBuildPassed)
            {
                state.AddTimeline("Production build passed; unresolved test-project issues were downgraded where applicable.");
                await _mcpAdapter.PublishStatusAsync("Proceeding with production changes despite unresolved test-project issues.");
            }

            if (AuditorAgent.HasBlockingFindings(state.Audit))
            {
                state.AddTimeline("Audit issue: unresolved blocking findings remain.");
                await _mcpAdapter.PublishStatusAsync("Audit has blocking findings; continuing workflow.");
            }
        }

        if (_acceptanceCriteriaOptions.Enabled
            && WorkflowStageResume.ShouldRun(startFrom, WorkflowStage.ValidatingAcceptance))
        {
            await RunAcceptanceStageAsync(state, cancellationToken);
        }
        else if (_acceptanceCriteriaOptions.Enabled)
        {
            LogSkippedStage(state, WorkflowStage.ValidatingAcceptance);
        }

        await FinalizeWorkflowAsync(state, cancellationToken);
        SaveCheckpoint(state);
        return state;
    }
}
