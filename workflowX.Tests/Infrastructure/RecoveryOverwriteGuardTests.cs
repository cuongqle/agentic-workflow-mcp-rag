using workflowX.Infrastructure;
using workflowX.Tests.Helpers;

namespace workflowX.Tests.Infrastructure;

public class RecoveryOverwriteGuardTests
{
    [Fact]
    public void TryValidateOverwrite_allows_new_file_during_recovery()
    {
        WorkflowState state = WorkflowStateBuilder.Create("/repo");
        state.Stage = WorkflowStage.Recovering;
        WorkflowStateBuilder.WithBuildFindings(
            state,
            new AgentFinding { Severity = FindingSeverity.High, Message = "src/A.cs(1,1): error CS0001: bad" });

        Assert.True(RecoveryOverwriteGuard.TryValidateOverwrite(
            state,
            "/repo",
            "src/B.cs",
            existedBefore: false,
            out _));
    }

    [Fact]
    public void TryValidateOverwrite_allows_overwrite_when_compiler_names_file()
    {
        WorkflowState state = WorkflowStateBuilder.Create("/repo");
        state.Stage = WorkflowStage.Recovering;
        WorkflowStateBuilder.WithBuildFindings(
            state,
            new AgentFinding
            {
                Severity = FindingSeverity.High,
                Message = "Shared/Infrastructure/BaseStore.cs(42,9): error CS1061: missing member"
            });

        Assert.True(RecoveryOverwriteGuard.TryValidateOverwrite(
            state,
            "/repo",
            "Shared/Infrastructure/BaseStore.cs",
            existedBefore: true,
            out _));
    }

    [Fact]
    public void TryValidateOverwrite_rejects_overwrite_when_file_not_in_compiler_output()
    {
        WorkflowState state = WorkflowStateBuilder.Create("/repo");
        state.Stage = WorkflowStage.Recovering;
        WorkflowStateBuilder.WithBuildFindings(
            state,
            new AgentFinding
            {
                Severity = FindingSeverity.High,
                Message = "Features/Timesheet/TimesheetController.cs(10,5): error CS0246: type not found"
            });

        Assert.False(RecoveryOverwriteGuard.TryValidateOverwrite(
            state,
            "/repo",
            "Shared/Infrastructure/BaseStore.cs",
            existedBefore: true,
            out string reason));
        Assert.Contains("no compiler error was reported", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryValidateOverwrite_ignores_guard_outside_recovery_stages()
    {
        WorkflowState state = WorkflowStateBuilder.Create("/repo");
        state.Stage = WorkflowStage.Implementing;
        WorkflowStateBuilder.WithBuildFindings(
            state,
            new AgentFinding
            {
                Severity = FindingSeverity.High,
                Message = "Features/Timesheet/TimesheetController.cs(10,5): error CS0246: type not found"
            });

        Assert.True(RecoveryOverwriteGuard.TryValidateOverwrite(
            state,
            "/repo",
            "Shared/Infrastructure/BaseStore.cs",
            existedBefore: true,
            out _));
    }
}
