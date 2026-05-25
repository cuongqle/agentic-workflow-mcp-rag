using workflowX.Configuration;
using workflowX.Tests.Helpers;

namespace workflowX.Tests.Infrastructure;

public class WorkflowStateCheckpointStoreTests
{
    [Fact]
    public void Save_and_load_round_trips_workflow_state()
    {
        using TempRepo repo = new();
        WorkflowState original = WorkflowStateBuilder.Create(repo.Path);
        original.Task = new WorkflowTask { Title = "Timesheet", Description = "Implement timesheet feature" };
        original.Stage = WorkflowStage.Implementing;
        original.RecoveryAttemptCount = 1;
        original.CombinedRagContext = "RAG context";
        original.RequirementsSpec = new RequirementsSpec
        {
            UserStory = "As a user",
            AcceptanceCriteria = [new AcceptanceCriterion("AC-1", "Build passes")]
        };
        original.ArchitecturePlan = new ArchitecturePlan
        {
            Rationale = "Add repository",
            BackendFiles = [new ArchitectureDeliverable("src/Repositories/TimesheetRepository.cs")]
        };
        original.Backend = new AgentResult
        {
            AgentName = "BackendDeveloperAgent",
            Summary = "Generated backend",
            ProposedFiles =
            [
                new GeneratedFile
                {
                    RelativePath = "src/Repositories/TimesheetRepository.cs",
                    Content = "public class TimesheetRepository {}"
                }
            ]
        };
        original.AppliedFiles.Add("src/Repositories/TimesheetRepository.cs");
        original.ComplianceIssues.Add("[High] missing test");
        original.AddTimeline("Implementation started.");

        WorkflowStateCheckpointStore.Save(original);
        WorkflowState? loaded = WorkflowStateCheckpointStore.TryLoad(
            repo.Path,
            original.Task.Description,
            new WorkflowResumeOptions { ResumeFromCheckpoint = true });

        Assert.NotNull(loaded);
        Assert.Equal(WorkflowStage.Implementing, loaded.Stage);
        Assert.Equal(1, loaded.RecoveryAttemptCount);
        Assert.Equal("RAG context", loaded.CombinedRagContext);
        Assert.NotNull(loaded.RequirementsSpec);
        Assert.Single(loaded.RequirementsSpec!.AcceptanceCriteria);
        Assert.NotNull(loaded.ArchitecturePlan);
        Assert.Single(loaded.Backend!.ProposedFiles);
        Assert.Single(loaded.AppliedFiles);
        Assert.Single(loaded.ComplianceIssues);
        Assert.Single(loaded.Timeline);
    }

    [Fact]
    public void TryLoad_ignores_checkpoint_when_task_hash_differs()
    {
        using TempRepo repo = new();
        WorkflowState state = WorkflowStateBuilder.Create(repo.Path);
        state.Task = new WorkflowTask { Title = "Task", Description = "Original task" };
        state.Stage = WorkflowStage.Planning;
        WorkflowStateCheckpointStore.Save(state);

        WorkflowState? loaded = WorkflowStateCheckpointStore.TryLoad(
            repo.Path,
            "Different task",
            new WorkflowResumeOptions { ResumeFromCheckpoint = true });

        Assert.Null(loaded);
    }

    [Fact]
    public void TryLoadFromArtifacts_builds_state_at_implementing()
    {
        using TempRepo repo = new();
        string outputDir = Path.Combine(repo.Path, "workflowX-output");
        Directory.CreateDirectory(outputDir);
        File.WriteAllText(
            Path.Combine(outputDir, "requirements.json"),
            """
            {
              "userStory": "As a user I want timesheets",
              "acceptanceCriteria": [{ "id": "AC-1", "description": "Build passes" }]
            }
            """);
        File.WriteAllText(
            Path.Combine(outputDir, "architecture-plan.json"),
            """
            {
              "summary": "Add timesheet module",
              "backendFiles": [{ "path": "src/Repositories/TimesheetRepository.cs", "description": "repo" }],
              "frontendFiles": []
            }
            """);

        WorkflowState? loaded = WorkflowStateCheckpointStore.TryLoadFromArtifacts(
            repo.Path,
            "Implement timesheet feature");

        Assert.NotNull(loaded);
        Assert.Equal(WorkflowStage.Implementing, loaded.Stage);
        Assert.NotNull(loaded.RequirementsSpec);
        Assert.NotNull(loaded.ArchitecturePlan);
        Assert.Single(loaded.ArchitecturePlan!.BackendFiles);
    }

    [Fact]
    public void WorkflowStageResume_skips_completed_stages()
    {
        Assert.False(WorkflowStageResume.ShouldRun(WorkflowStage.Implementing, WorkflowStage.Requirements));
        Assert.False(WorkflowStageResume.ShouldRun(WorkflowStage.Implementing, WorkflowStage.Planning));
        Assert.True(WorkflowStageResume.ShouldRun(WorkflowStage.Implementing, WorkflowStage.Implementing));
        Assert.True(WorkflowStageResume.ShouldRun(WorkflowStage.Implementing, WorkflowStage.Integrating));
    }

    [Fact]
    public void WorkflowCliArgs_parses_resume_flags()
    {
        WorkflowCliArgs.ParsedArgs parsed = WorkflowCliArgs.Parse(
            ["--from", "Recovering", "--no-resume", "Implement", "timesheet"],
            "default prompt",
            new WorkflowResumeOptions { ResumeFromCheckpoint = true });

        Assert.Equal("Implement timesheet", parsed.TaskPrompt);
        Assert.False(parsed.ResumeOptions.ResumeFromCheckpoint);
        Assert.Equal(WorkflowStage.Recovering, parsed.ResumeOptions.StartFromStage);
    }
}
