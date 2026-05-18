interface IWorkflowAgent
{
    string Name { get; }
    Task<AgentResult> ExecuteAsync(WorkflowState state, CancellationToken cancellationToken = default);
}
