using workflowX.Tests.Helpers;

namespace workflowX.Tests.Infrastructure;

public class WorkflowArtifactWriterTests
{
    [Fact]
    public void WriteArchitectureArtifacts_writes_markdown_and_json_under_target_repo()
    {
        using TempRepo repo = new();
        WorkflowState state = WorkflowStateBuilder.Create(repo.Path);
        state.ArchitecturePlan = new ArchitecturePlan
        {
            Rationale = "Add CRUD for timesheets.",
            BackendFiles = [new ArchitectureDeliverable("src/Repositories/TimesheetRepository.cs", "data access")],
            FrontendFiles = [new ArchitectureDeliverable("web/modules/timesheets.js", "controller")],
            TestStrategy = "Run unit tests",
            RollbackNotes = "Revert commit"
        };
        state.Architecture = new AgentResult
        {
            AgentName = "ArchitectureAgent",
            Summary = "Add CRUD for timesheets.",
            ArchitecturePlan = state.ArchitecturePlan
        };

        string outputDir = WorkflowArtifactWriter.WriteArchitectureArtifacts(state);

        Assert.Equal(Path.Combine(repo.Path, "workflowX-output"), outputDir);
        Assert.True(File.Exists(Path.Combine(outputDir, "architecture-plan.md")));
        Assert.True(File.Exists(Path.Combine(outputDir, "architecture-plan.json")));
        Assert.True(File.Exists(Path.Combine(outputDir, "architecture-agent.md")));

        string markdown = File.ReadAllText(Path.Combine(outputDir, "architecture-plan.md"));
        Assert.Contains("TimesheetRepository.cs", markdown);
        Assert.Contains("BACKEND_FILES:", markdown);

        string json = File.ReadAllText(Path.Combine(outputDir, "architecture-plan.json"));
        Assert.Contains("timesheets.js", json);
    }
}
