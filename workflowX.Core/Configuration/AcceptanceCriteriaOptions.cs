namespace workflowX.Configuration;

public sealed class AcceptanceCriteriaOptions
{
    public bool Enabled { get; init; } = true;
    public int MinimumCriteriaCount { get; init; } = 1;
    public bool RequireProductionBuildPass { get; init; } = true;
}
