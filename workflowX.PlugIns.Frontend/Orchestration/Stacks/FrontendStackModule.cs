using workflowX.Infrastructure;

/// <summary>
/// Frontend stack plug-in — compliance rules; no test quarantine policy yet.
/// </summary>
sealed class FrontendStackModule : IStackModule
{
    public string StackId => "Frontend";

    public bool IsActive(RepoStack stack) => stack.Frontend;

    public IReadOnlyList<IComplianceRule> ComplianceRules => FrontendComplianceRules.All;

    public ITestReleasePolicy? TestReleasePolicy => null;
}
