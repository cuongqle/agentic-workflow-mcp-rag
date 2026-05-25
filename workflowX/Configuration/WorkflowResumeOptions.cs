namespace workflowX.Configuration;

public sealed class WorkflowResumeOptions
{
    public bool ResumeFromCheckpoint { get; init; } = true;
    public WorkflowStage? StartFromStage { get; init; }
    public string? CheckpointPath { get; init; }

    public WorkflowStage ResolveStartStage(WorkflowState state)
    {
        if (StartFromStage is WorkflowStage explicitStage)
        {
            return explicitStage;
        }

        if (state.Stage is WorkflowStage.Queued)
        {
            return WorkflowStage.Requirements;
        }

        return state.Stage;
    }
}
