using workflowX.Infrastructure;
using workflowX.Tests.Helpers;

namespace workflowX.PlugIns.DotNet.Tests.Orchestration;

public class DotNetTestReleasePolicySupportTests
{
    [Fact]
    public void ShouldAttemptQuarantine_true_for_dotnet_test_only_failures()
    {
        WorkflowState state = WorkflowStateBuilder.Create("/repo", stack: new RepoStack(true, false));
        WorkflowStateBuilder.WithBuildFindings(
            state,
            productionBuildPassed: true,
            new AgentFinding
            {
                Severity = FindingSeverity.High,
                Message = "MyApp.UnitTest/Repositories/FooTests.cs(2,1): error CS0246: missing type"
            });

        Assert.True(TestReleasePolicySupport.ShouldAttemptQuarantine(state));
    }
}
