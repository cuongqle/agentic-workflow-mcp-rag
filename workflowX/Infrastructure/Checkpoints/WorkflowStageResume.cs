namespace workflowX.Infrastructure;

internal static class WorkflowStageResume
{
    public static WorkflowStage NormalizeStart(WorkflowStage stage) =>
        stage switch
        {
            WorkflowStage.Queued => WorkflowStage.Requirements,
            WorkflowStage.ReadyForPR or WorkflowStage.Done or WorkflowStage.Blocked => WorkflowStage.Requirements,
            _ => stage
        };

    public static bool ShouldRun(WorkflowStage startFrom, WorkflowStage stage)
    {
        startFrom = NormalizeStart(startFrom);
        return StageOrder(stage) >= StageOrder(startFrom);
    }

    public static bool ShouldEnterRecoveryLoop(WorkflowStage startFrom, WorkflowState state)
    {
        startFrom = NormalizeStart(startFrom);
        if (startFrom == WorkflowStage.Recovering && state.Audit is not null)
        {
            return true;
        }

        return ShouldRun(startFrom, WorkflowStage.Auditing)
               && AuditorAgent.HasBlockingFindings(state.Audit);
    }

    private static int StageOrder(WorkflowStage stage) =>
        stage switch
        {
            WorkflowStage.Queued => 0,
            WorkflowStage.Requirements => 1,
            WorkflowStage.Planning => 2,
            WorkflowStage.Implementing => 3,
            WorkflowStage.Integrating => 4,
            WorkflowStage.Auditing => 5,
            WorkflowStage.Recovering => 6,
            WorkflowStage.ValidatingAcceptance => 7,
            WorkflowStage.ReadyForPR => 8,
            WorkflowStage.Done => 9,
            WorkflowStage.Blocked => 10,
            _ => 0
        };
}
