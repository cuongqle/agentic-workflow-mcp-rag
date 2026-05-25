using workflowX.Infrastructure;
using workflowX.Tests.Helpers;

namespace workflowX.PlugIns.Frontend.Tests.Orchestration;

public class FrontendTestReleasePolicySupportTests
{
    [Fact]
    public void ShouldAttemptQuarantine_false_for_frontend_only_repo()
    {
        WorkflowState state = WorkflowStateBuilder.Create("/repo", stack: new RepoStack(false, true));
        WorkflowStateBuilder.WithBuildFindings(
            state,
            productionBuildPassed: true,
            new AgentFinding
            {
                Severity = FindingSeverity.High,
                Message = "MyApp.UnitTest/FooTests.cs(1,1): error CS0001"
            });

        Assert.False(TestReleasePolicySupport.ShouldAttemptQuarantine(state));
    }
}
