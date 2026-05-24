using agents_mcp_rag.Infrastructure;
using agents_mcp_rag.Tests.Helpers;

namespace agents_mcp_rag.Tests.Orchestration;

public class WorkflowFindingRulesTests
{
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
    public void ResolveImplementationScope_with_markdown_bold_numbered_lists_parses_both_layers()
    {
        WorkflowState state = WorkflowStateBuilder.Create("/repo", stack: new RepoStack(true, true));
        state.Architecture = new AgentResult
        {
            Summary = """
                **BACKEND_FILES:**

                1. SinglePageSample.Repository/Entities/Timesheet.cs: entity
                2. SinglePageSample.WebAPI/Controllers/TimesheetController.cs: controller

                **FRONTEND_FILES:**

                1. SinglePageSample/Application/modules/sample/controllers/timesheets.js: controller
                2. SinglePageSample/Application/modules/sample/views/timesheets.html: view
                """
        };

        ImplementationScopeDetails details = WorkflowFindingRules.ResolveImplementationScopeDetails(state);

        Assert.True(details.Scope.RunBackend);
        Assert.True(details.Scope.RunFrontend);
        Assert.Equal("markdown-parsed", details.PlanSource);
        Assert.Equal(2, details.BackendPathCount);
        Assert.Equal(2, details.FrontendPathCount);
    }

    [Fact]
    public void ResolveImplementationScope_with_structured_plan_uses_structured_plan_source()
    {
        WorkflowState state = WorkflowStateBuilder.Create("/repo", stack: new RepoStack(true, true));
        state.ArchitecturePlan = new ArchitecturePlan
        {
            BackendFiles = [new ArchitectureDeliverable("src/Repositories/TimesheetRepository.cs")]
        };
        state.Architecture = new AgentResult { Summary = "Structured plan summary." };

        ImplementationScopeDetails details = WorkflowFindingRules.ResolveImplementationScopeDetails(state);

        Assert.True(details.Scope.RunBackend);
        Assert.False(details.Scope.RunFrontend);
        Assert.Equal("structured-plan", details.PlanSource);
        Assert.Equal(1, details.BackendPathCount);
    }

    [Fact]
    public void FormatImplementationScopeDiagnostics_includes_repo_and_deliverable_counts()
    {
        var details = new ImplementationScopeDetails(
            Scope: (true, false),
            ProjectHasBackend: true,
            ProjectHasFrontend: true,
            NeedsBackend: true,
            NeedsFrontend: false,
            BackendPathCount: 2,
            FrontendPathCount: 0,
            PlanSource: "markdown-parsed");

        string message = WorkflowFindingRules.FormatImplementationScopeDiagnostics(details);

        Assert.Contains("backend=True", message);
        Assert.Contains("frontend=False", message);
        Assert.Contains("deliverables backend=2", message);
        Assert.Contains("source=markdown-parsed", message);
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
