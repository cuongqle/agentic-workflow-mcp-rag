using workflowX.Infrastructure;
using workflowX.Tests.Helpers;

namespace workflowX.Tests.Infrastructure;

public class RecoveryOverwriteGuardTests
{
    private static readonly RepoStack DotNetStack = new(DotNet: true, Frontend: false);

    private static bool TryValidateOverwrite(
        WorkflowState state,
        string repoPath,
        string relativePath,
        bool existedBefore,
        out string reason) =>
        RecoveryApplySupport.TryValidateDotNetRecoveryOverwrite(
            state,
            DotNetStack,
            repoPath,
            relativePath,
            existedBefore,
            out reason);

    [Fact]
    public void TryValidateOverwrite_allows_new_file_during_recovery()
    {
        WorkflowState state = WorkflowStateBuilder.Create("/repo");
        state.Stage = WorkflowStage.Recovering;
        WorkflowStateBuilder.WithBuildFindings(
            state,
            new AgentFinding { Severity = FindingSeverity.High, Message = "src/A.cs(1,1): error CS0001: bad" });

        Assert.True(TryValidateOverwrite(
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

        Assert.True(TryValidateOverwrite(
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

        Assert.False(TryValidateOverwrite(
            state,
            "/repo",
            "Shared/Infrastructure/BaseStore.cs",
            existedBefore: true,
            out string reason));
        Assert.Contains("no compiler error was reported", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildAllowedRecoveryOverwritePaths_includes_owning_csproj_for_compiler_error()
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

        HashSet<string> allowed = RecoveryApplySupport.BuildCompilerReferencedRecoveryPaths(state, repo.Path);
        Assert.Contains("Acme.Tests/Acme.Tests.csproj", allowed, StringComparer.OrdinalIgnoreCase);
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

        Assert.True(TryValidateOverwrite(
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

        Assert.True(TryValidateOverwrite(
            state,
            repo.Path,
            "Acme/Acme.Tests/Acme.Tests.csproj",
            existedBefore: true,
            out _));
    }

    [Fact]
    public void TryValidateOverwrite_allows_production_csproj_when_compiler_names_cs_in_same_project()
    {
        using TempRepo repo = new();
        repo.WriteFile(
            "SinglePageSample.Repository/SinglePageSample.Repository.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
        repo.WriteFile("SinglePageSample.Repository/TimesheetRepository.cs", "class TimesheetRepository { }");

        WorkflowState state = WorkflowStateBuilder.Create(repo.Path);
        state.Stage = WorkflowStage.Recovering;
        WorkflowStateBuilder.WithBuildFindings(
            state,
            new AgentFinding
            {
                Severity = FindingSeverity.High,
                Message =
                    "SinglePageSample.Repository/TimesheetRepository.cs(1,7): error CS0246: The type or namespace name 'Example' could not be found"
            });

        Assert.True(TryValidateOverwrite(
            state,
            repo.Path,
            "SinglePageSample.Repository/SinglePageSample.Repository.csproj",
            existedBefore: true,
            out _));
    }

    [Fact]
    public void TryValidateOverwrite_allows_planned_test_during_missing_test_recovery()
    {
        using TempRepo repo = new();
        repo.WriteFile(
            "SinglePageSample.UnitTest/SinglePageSample.UnitTest.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
              </ItemGroup>
            </Project>
            """);
        repo.WriteFile(
            "SinglePageSample.UnitTest/RepositoryTest/TimesheetRepositoryTests.cs",
            "class TimesheetRepositoryTests { }");

        WorkflowState state = WorkflowStateBuilder.Create(repo.Path);
        state.Stage = WorkflowStage.Recovering;
        state.ArchitecturePlan = new ArchitecturePlan
        {
            BackendFiles =
            [
                new ArchitectureDeliverable("SinglePageSample.UnitTest/RepositoryTest/TimesheetRepositoryTests.cs")
            ]
        };
        state.Audit = new AgentResult
        {
            Findings =
            [
                new AgentFinding
                {
                    Severity = FindingSeverity.High,
                    Message = "Missing unit tests for Timesheet feature"
                }
            ]
        };
        state.BuildValidation = new AgentResult
        {
            AgentName = "BuildValidationAgent",
            TestsPassed = false,
            ProductionBuildPassed = true,
            Findings = []
        };

        Assert.True(TryValidateOverwrite(
            state,
            repo.Path,
            "SinglePageSample.UnitTest/RepositoryTest/TimesheetRepositoryTests.cs",
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

        Assert.True(TryValidateOverwrite(
            state,
            "/repo",
            "Shared/Infrastructure/BaseStore.cs",
            existedBefore: true,
            out _));
    }
}
