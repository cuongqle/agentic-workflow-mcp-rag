using workflowX.Infrastructure;
using workflowX.Tests.Helpers;

namespace workflowX.PlugIns.DotNet.Tests.Orchestration;

public class DotNetWorkflowFindingRulesTests
{
    [Fact]
    public void HasUnresolvedCompilationProblems_true_for_apply_rejection_without_build()
    {
        WorkflowState state = WorkflowStateBuilder.Create("/repo", stack: new RepoStack(true, false));
        state.ComplianceIssues.Add("Apply rejected 'src/Foo.cs': missing interface member");

        Assert.True(WorkflowFindingRules.HasUnresolvedCompilationProblems(state));
    }

    [Fact]
    public void HasActionableBuildFindings_dotnet_ignores_build_failed_banner()
    {
        WorkflowState state = WorkflowStateBuilder.Create("/repo", stack: new RepoStack(true, false));
        WorkflowStateBuilder.WithBuildFindings(
            state,
            new AgentFinding { Severity = FindingSeverity.High, Message = "Build FAILED." },
            new AgentFinding { Severity = FindingSeverity.High, Message = "src/A.cs(1,1): error CS0001: bad" });

        Assert.True(WorkflowFindingRules.HasActionableBuildFindings(state));
    }

    [Fact]
    public void HasActionableBuildFindings_dotnet_false_when_only_banner()
    {
        WorkflowState state = WorkflowStateBuilder.Create("/repo", stack: new RepoStack(true, false));
        WorkflowStateBuilder.WithBuildFindings(
            state,
            new AgentFinding { Severity = FindingSeverity.High, Message = "Build FAILED." });

        Assert.False(WorkflowFindingRules.HasActionableBuildFindings(state));
    }
}
