using workflowX.Infrastructure;
using workflowX.Tests.Helpers;

namespace workflowX.Tests.Orchestration;

public class MissingTestRecoverySupportTests
{
    [Fact]
    public void ShouldFocusOnMissingTests_when_tests_failed()
    {
        var state = WorkflowStateBuilder.Create("/repo", stack: new RepoStack(DotNet: true, Frontend: false));
        state.Stage = WorkflowStage.Recovering;
        state.BuildValidation = new AgentResult
        {
            AgentName = "BuildValidationAgent",
            TestsPassed = false,
            Findings =
            [
                new AgentFinding
                {
                    Severity = FindingSeverity.High,
                    Message = "Automated tests failed (dotnet test); inspect test output for details."
                }
            ]
        };

        Assert.True(MissingTestRecoverySupport.ShouldFocusOnMissingTests(state));
    }

    [Fact]
    public void BuildPromptSection_lists_missing_planned_test_paths()
    {
        var state = WorkflowStateBuilder.Create("/repo", stack: new RepoStack(DotNet: true, Frontend: false));
        state.Stage = WorkflowStage.Recovering;
        state.RepoPath = Path.Combine(Path.GetTempPath(), "workflowx-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(state.RepoPath);
        try
        {
            state.ArchitecturePlan = new ArchitecturePlan
            {
                BackendFiles =
                [
                    new ArchitectureDeliverable("SinglePageSample.Repository/Repositories/TimesheetRepository.cs"),
                    new ArchitectureDeliverable("SinglePageSample.UnitTest/RepositoryTest/TimesheetRepositoryTests.cs")
                ]
            };
            state.BuildValidation = new AgentResult
            {
                AgentName = "BuildValidationAgent",
                TestsPassed = false,
                Findings = []
            };

            string section = MissingTestRecoverySupport.BuildPromptSection(state);

            Assert.Contains("TimesheetRepositoryTests.cs", section, StringComparison.Ordinal);
            Assert.Contains("missing on disk", section, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(state.RepoPath, recursive: true);
        }
    }

    [Fact]
    public void ShouldRestrictAllowedFiles_true_when_tests_failed_and_audit_blocking()
    {
        var state = WorkflowStateBuilder.Create("/repo", stack: new RepoStack(DotNet: true, Frontend: false));
        state.Stage = WorkflowStage.Recovering;
        state.Audit = new AgentResult
        {
            Findings =
            [
                new AgentFinding
                {
                    Severity = FindingSeverity.High,
                    Message = "Missing unit tests for TimesheetRepository"
                }
            ]
        };
        WorkflowStateBuilder.WithBuildFindings(
            state,
            productionBuildPassed: true,
            new AgentFinding
            {
                Severity = FindingSeverity.High,
                Message = "Automated tests failed (dotnet test); inspect test output for details."
            });
        state.BuildValidation = new AgentResult
        {
            AgentName = state.BuildValidation!.AgentName,
            Summary = state.BuildValidation.Summary,
            ProductionBuildPassed = state.BuildValidation.ProductionBuildPassed,
            TestsPassed = false,
            Findings = state.BuildValidation.Findings
        };

        Assert.True(RecoveryContextSupport.ShouldRestrictAllowedFilesToBuildErrors(state));
    }
}
