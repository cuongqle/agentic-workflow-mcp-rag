using workflowX.Infrastructure;
using workflowX.Tests.Helpers;

namespace workflowX.PlugIns.DotNet.Tests.Orchestration;

public class DotNetTestReleasePolicySupportTests
{
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

    [Fact]
    public async Task TryQuarantineAsync_skips_architecture_deliverable_test_files()
    {
        using var repo = new TempRepo();
        string testPath = "SinglePageSample.UnitTest/RepositoryTest/TimesheetRepositoryTests.cs";
        string testFullPath = Path.Combine(repo.Path, testPath);
        Directory.CreateDirectory(Path.GetDirectoryName(testFullPath)!);
        const string brokenContent = "// broken test";
        await File.WriteAllTextAsync(testFullPath, brokenContent);

        WorkflowState state = WorkflowStateBuilder.Create(repo.Path, stack: new RepoStack(true, false));
        state.ArchitecturePlan = new ArchitecturePlan
        {
            BackendFiles =
            [
                new ArchitectureDeliverable(testPath, "Timesheet repository tests")
            ]
        };
        state.BuildValidation = new AgentResult
        {
            ProductionBuildPassed = true,
            Findings =
            [
                new AgentFinding
                {
                    Severity = FindingSeverity.High,
                    Message = $"{testPath}(2,1): error CS0246: missing type"
                }
            ]
        };

        var rollbackChanges = new Dictionary<string, AppliedFileChange>(StringComparer.OrdinalIgnoreCase)
        {
            [testPath] = new AppliedFileChange(testPath, ExistedBeforeApply: false, PreviousContent: null)
        };

        bool quarantined = await TestReleasePolicySupport.TryQuarantineAsync(
            state,
            rollbackChanges,
            (_, _) => Task.FromResult(state.BuildValidation!),
            _ => Task.CompletedTask,
            CancellationToken.None);

        Assert.False(quarantined);
        Assert.Equal(brokenContent, await File.ReadAllTextAsync(testFullPath));
        Assert.Contains(testPath, rollbackChanges.Keys, StringComparer.OrdinalIgnoreCase);
    }
}
