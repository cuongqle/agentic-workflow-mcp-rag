using workflowX.Infrastructure;

/// <summary>
/// DotNet stack plug-in — compliance rules (prompt-first; no test quarantine policy).
/// </summary>
sealed class DotNetStackModule : IStackModule
{
    public string StackId => "DotNet";

    public bool IsActive(RepoStack stack) => stack.DotNet;

    public IReadOnlyList<IComplianceRule> ComplianceRules { get; } = Array.Empty<IComplianceRule>();

    public ITestReleasePolicy? TestReleasePolicy => null;
}
