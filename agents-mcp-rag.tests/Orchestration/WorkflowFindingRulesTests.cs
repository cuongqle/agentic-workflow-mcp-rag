using agents_mcp_rag.Infrastructure;
using agents_mcp_rag.tests.Helpers;

namespace agents_mcp_rag.tests.Orchestration;

public class WorkflowFindingRulesTests
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

    [Fact]
    public void HasActionableBuildFindings_frontend_uses_high_blocker_only()
    {
        WorkflowState state = WorkflowStateBuilder.Create("/repo", stack: new RepoStack(false, true));
        WorkflowStateBuilder.WithBuildFindings(
            state,
            new AgentFinding { Severity = FindingSeverity.Medium, Message = "ERROR in ./app.js" });

        Assert.False(WorkflowFindingRules.HasActionableBuildFindings(state));
    }

    [Fact]
    public void ResolveImplementationScope_defaults_to_repo_capabilities_when_architecture_silent()
    {
        WorkflowState state = WorkflowStateBuilder.Create("/repo", stack: new RepoStack(true, true));
        state.Architecture = new AgentResult { Summary = "Plan without explicit file sections." };

        (bool runBackend, bool runFrontend) = WorkflowFindingRules.ResolveImplementationScope(state);

        Assert.True(runBackend);
        Assert.True(runFrontend);
    }

    [Fact]
    public void ResolveImplementationScope_honors_architecture_backend_only()
    {
        WorkflowState state = WorkflowStateBuilder.Create("/repo", stack: new RepoStack(true, true));
        state.Architecture = new AgentResult
        {
            Summary = """
                BACKEND_FILES:
                - src/Repositories/TimesheetRepository.cs
                """
        };

        (bool runBackend, bool runFrontend) = WorkflowFindingRules.ResolveImplementationScope(state);

        Assert.True(runBackend);
        Assert.False(runFrontend);
    }

    [Fact]
    public void StripArchitectureCodeBlocks_removes_fenced_code()
    {
        const string input = "Plan intro\n```csharp\nclass X {}\n```\n\nTail";
        string stripped = WorkflowFindingRules.StripArchitectureCodeBlocks(input);
        Assert.DoesNotContain("class X", stripped);
        Assert.Contains("Plan intro", stripped);
        Assert.Contains("Tail", stripped);
    }

    [Fact]
    public void GetAllProposedFiles_merges_backend_frontend_and_recovery()
    {
        WorkflowState state = WorkflowStateBuilder.Create("/repo");
        state.Backend = new AgentResult
        {
            ProposedFiles = [new GeneratedFile { RelativePath = "a.cs", Content = "a" }]
        };
        state.Frontend = new AgentResult
        {
            ProposedFiles = [new GeneratedFile { RelativePath = "b.js", Content = "b" }]
        };
        state.Recovery = new AgentResult
        {
            ProposedFiles = [new GeneratedFile { RelativePath = "c.cs", Content = "c" }]
        };

        List<GeneratedFile> all = WorkflowFindingRules.GetAllProposedFiles(state);

        Assert.Equal(3, all.Count);
    }
}
