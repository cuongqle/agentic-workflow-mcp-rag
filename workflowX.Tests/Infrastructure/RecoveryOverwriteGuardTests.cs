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
    public void TryValidateOverwrite_allows_test_csproj_when_tests_cs_has_compiler_error()
    {
        using TempRepo repo = new();
        repo.WriteFile(
            "Acme.Tests/Acme.Tests.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
              </ItemGroup>
            </Project>
            """);
        repo.WriteFile("Acme.Tests/TimesheetRepositoryTests.cs", "class TimesheetRepositoryTests { }");

        WorkflowState state = WorkflowStateBuilder.Create(repo.Path);
        state.Stage = WorkflowStage.Recovering;
        WorkflowStateBuilder.WithBuildFindings(
            state,
            new AgentFinding
            {
                Severity = FindingSeverity.High,
                Message =
                    "Acme.Tests/TimesheetRepositoryTests.cs(1,7): error CS0246: The type or namespace name 'FluentAssertions' could not be found"
            });

        Assert.True(RecoveryOverwriteGuard.TryValidateOverwrite(
            state,
            repo.Path,
            "Acme.Tests/Acme.Tests.csproj",
            existedBefore: true,
            out _));
    }

    [Fact]
    public void TryValidateOverwrite_allows_test_csproj_when_agent_duplicates_repo_folder_prefix()
    {
        using TempRepo repo = new();
        repo.WriteFile(
            "Acme.Tests/Acme.Tests.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
              </ItemGroup>
            </Project>
            """);
        repo.WriteFile("Acme.Tests/TimesheetRepositoryTests.cs", "class TimesheetRepositoryTests { }");

        WorkflowState state = WorkflowStateBuilder.Create(repo.Path);
        state.Stage = WorkflowStage.Recovering;
        WorkflowStateBuilder.WithBuildFindings(
            state,
            new AgentFinding
            {
                Severity = FindingSeverity.High,
                Message =
                    "Acme.Tests/TimesheetRepositoryTests.cs(1,7): error CS0246: The type or namespace name 'Example' could not be found"
            });

        Assert.True(RecoveryOverwriteGuard.TryValidateOverwrite(
            state,
            repo.Path,
            "Acme/Acme.Tests/Acme.Tests.csproj",
            existedBefore: true,
            out _));
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
