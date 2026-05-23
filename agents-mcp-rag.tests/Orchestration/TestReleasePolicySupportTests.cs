using agents_mcp_rag.Infrastructure;
using agents_mcp_rag.Tests.Helpers;

namespace agents_mcp_rag.Tests.Orchestration;

public class TestReleasePolicySupportTests
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
