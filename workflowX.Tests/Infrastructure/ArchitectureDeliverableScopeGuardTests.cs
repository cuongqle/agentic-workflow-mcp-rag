using workflowX.Infrastructure;

namespace workflowX.Tests.Infrastructure;

public class ArchitectureDeliverableScopeGuardTests
{
    [Fact]
    public void TryValidatePath_rejects_unplanned_and_wrong_prefix_paths()
    {
        string repoPath = Path.Combine(Path.GetTempPath(), $"workflowx-scope-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repoPath);

        var state = new WorkflowState
        {
            RepoPath = repoPath,
            Stage = WorkflowStage.Implementing,
            ArchitecturePlan = new ArchitecturePlan
            {
                BackendFiles =
                [
                    new ArchitectureDeliverable("SinglePageSample.Repository/TimesheetRepository.cs", "repo"),
                    new ArchitectureDeliverable(
                        "SinglePageSample.UnitTest/RepositoryTest/TimesheetRepositoryTests.cs",
                        "tests"),
                    new ArchitectureDeliverable(
                        "SinglePageSample.WebAPI/Controllers/TimesheetController.cs",
                        "controller")
                ]
            }
        };

        RepoStack stack = new(DotNet: true, Frontend: false);

        Assert.False(ArchitectureDeliverableScopeGuard.TryValidatePath(
            state,
            "SinglePageSample/SinglePageSample.cs",
            stack,
            out string reason));
        Assert.Contains("architecture plan", reason, StringComparison.OrdinalIgnoreCase);

        Assert.False(ArchitectureDeliverableScopeGuard.TryValidatePath(
            state,
            "SinglePageSample/SinglePageSample.WebAPI/Controllers/TimesheetController.cs",
            stack,
            out _));

        Assert.False(ArchitectureDeliverableScopeGuard.TryValidatePath(
            state,
            "SinglePageSample.WebAPI/SinglePageSample.WebAPI.csproj",
            stack,
            out _));

        Assert.True(ArchitectureDeliverableScopeGuard.TryValidatePath(
            state,
            "SinglePageSample.Repository/Interfaces/ITimesheetRepository.cs",
            stack,
            out _));

        Assert.True(ArchitectureDeliverableScopeGuard.TryValidatePath(
            state,
            "SinglePageSample.WebAPI/Controllers/TimesheetController.cs",
            stack,
            out _));

        Assert.True(ArchitectureDeliverableScopeGuard.TryValidatePath(
            state,
            "SinglePageSample.UnitTest/RepositoryTest/TimesheetRepositoryTests.cs",
            stack,
            out _));
    }
}
