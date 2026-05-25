using workflowX.Infrastructure;
using workflowX.Orchestration.Compliance;

namespace workflowX.Orchestration.Stacks;

/// <summary>
/// Plug-in surface for a discovered stack (DotNet, Frontend, future Python, …).
/// Core orchestration routes through <see cref="StackModuleRegistry"/> — no direct stack-type references.
/// </summary>
public interface IStackModule
{
    string StackId { get; }

    bool IsActive(RepoStack stack);

    IReadOnlyList<IComplianceRule> ComplianceRules { get; }

    ITestReleasePolicy? TestReleasePolicy { get; }
}
