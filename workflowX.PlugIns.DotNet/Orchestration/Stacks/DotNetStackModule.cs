using workflowX.Infrastructure;

/// <summary>
/// DotNet stack plug-in — compliance rules and test-release policy.
/// </summary>
sealed class DotNetStackModule : IStackModule
{
    public string StackId => "DotNet";

    public bool IsActive(RepoStack stack) => stack.DotNet;

    public IReadOnlyList<IComplianceRule> ComplianceRules => DotNetComplianceRules.All;

    public ITestReleasePolicy? TestReleasePolicy { get; } = new DotNetTestReleasePolicy();
}
