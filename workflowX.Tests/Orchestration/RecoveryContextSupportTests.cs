using workflowX.Infrastructure;
using workflowX.Tests.Helpers;

namespace workflowX.Tests.Orchestration;

public class RecoveryContextSupportTests
{
    [Fact]
    public void ShouldRestrictAllowedFilesToBuildErrors_when_recovering_with_audit_only()
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
                    Message = "Missing TimesheetRepositoryTests.cs"
                }
            ]
        };
        state.BuildValidation = new AgentResult
        {
            AgentName = "BuildValidationAgent",
            Summary = "passed",
            Findings = []
        };

        Assert.True(RecoveryContextSupport.ShouldRestrictAllowedFilesToBuildErrors(state));
    }

    [Fact]
    public void ShouldRestrictAllowedFilesToBuildErrors_false_when_build_failing_with_compiler_paths_only()
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
                    Message = "Controller action lacks input validation."
                }
            ]
        };
        state.BuildValidation = new AgentResult
        {
            AgentName = "BuildValidationAgent",
            ProductionBuildPassed = true,
            TestsPassed = true,
            Findings =
            [
                new AgentFinding
                {
                    Severity = FindingSeverity.High,
                    Message = "SinglePageSample.WebAPI/Controllers/TimesheetController.cs(10,1): error CS0246: missing type"
                }
            ]
        };

        Assert.False(RecoveryContextSupport.ShouldRestrictAllowedFilesToBuildErrors(state));
    }

    [Fact]
    public void CollectCompilerReferencedPaths_empty_when_build_passed()
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
                    Message = "Missing TimesheetRepositoryTests.cs"
                }
            ]
        };
        state.BuildValidation = new AgentResult { Findings = [] };
        state.CompilationFixAllowedFiles =
        [
            "SinglePageSample.Repository/Entities/Timesheet.cs",
            "SinglePageSample.Repository/Interfaces/ITimesheetRepository.cs"
        ];

        Assert.True(RecoveryContextSupport.ShouldRestrictAllowedFilesToBuildErrors(state));
        state.CompilationFixAllowedFiles = RecoveryContextSupport.CollectCompilerReferencedPaths(state);

        Assert.Empty(state.CompilationFixAllowedFiles);
    }
}
