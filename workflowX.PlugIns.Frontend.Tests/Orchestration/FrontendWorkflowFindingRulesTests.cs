using workflowX.Infrastructure;
using workflowX.Tests.Helpers;

namespace workflowX.PlugIns.Frontend.Tests.Orchestration;

public class FrontendWorkflowFindingRulesTests
{
    [Fact]
    public void HasActionableBuildFindings_frontend_uses_high_blocker_only()
    {
        WorkflowState state = WorkflowStateBuilder.Create("/repo", stack: new RepoStack(false, true));
        WorkflowStateBuilder.WithBuildFindings(
            state,
            new AgentFinding { Severity = FindingSeverity.Medium, Message = "ERROR in ./app.js" });

        Assert.False(WorkflowFindingRules.HasActionableBuildFindings(state));
    }
}
