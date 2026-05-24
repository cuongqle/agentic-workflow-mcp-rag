using agents_mcp_rag.Infrastructure;
using agents_mcp_rag.Tests.Helpers;

namespace agents_mcp_rag.PlugIns.Frontend.Tests.Orchestration;

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
