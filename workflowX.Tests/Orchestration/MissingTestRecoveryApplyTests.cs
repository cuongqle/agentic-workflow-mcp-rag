using workflowX.Infrastructure;
using workflowX.Tests.Helpers;

namespace workflowX.Tests.Orchestration;

public class MissingTestRecoveryApplyTests
{
    private static readonly RepoStack DotNetStack = new(DotNet: true, Frontend: false);

    [Fact]
    public void TryValidateOverwrite_allows_test_files_during_integrating_when_audit_flags_missing_tests()
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
            "SinglePageSample.Repository/SinglePageSample.Repository.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
        repo.WriteFile("SinglePageSample.Repository/TimesheetRepository.cs", "class TimesheetRepository { }");
        repo.WriteFile(
            "SinglePageSample.UnitTest/RepositoryTest/TimesheetRepositoryTests.cs",
            "class TimesheetRepositoryTests { }");
        repo.WriteFile(
            "SinglePageSample.UnitTest/RepositoryTest/EmployeeRepositoryTests.cs",
            "class EmployeeRepositoryTests { }");
        repo.WriteFile("SinglePageSample.Repository/EmployeeRepository.cs", "class EmployeeRepository { }");

        WorkflowState state = WorkflowStateBuilder.Create(repo.Path, stack: DotNetStack);
        state.Stage = WorkflowStage.Integrating;
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
            ProductionBuildPassed = true,
            TestsPassed = false,
            Findings = []
        };
        state.AppliedFiles.Add("SinglePageSample.Repository/TimesheetRepository.cs");

        Assert.True(RecoveryApplySupport.TryValidateDotNetRecoveryOverwrite(
            state,
            DotNetStack,
            repo.Path,
            "SinglePageSample.UnitTest/RepositoryTest/TimesheetRepositoryTests.cs",
            existedBefore: true,
            out string testReason),
            testReason);

        Assert.False(RecoveryApplySupport.TryValidateDotNetRecoveryOverwrite(
            state,
            DotNetStack,
            repo.Path,
            "SinglePageSample.Repository/TimesheetRepository.cs",
            existedBefore: true,
            out string productionReason),
            productionReason);
    }

    [Fact]
    public void CollectRecoveryTestOverwritePaths_discovers_test_from_exemplar_pair_when_plan_empty()
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
            "SinglePageSample.UnitTest/RepositoryTest/EmployeeRepositoryTests.cs",
            "class EmployeeRepositoryTests { }");
        repo.WriteFile("SinglePageSample.Repository/EmployeeRepository.cs", "class EmployeeRepository { }");

        WorkflowState state = WorkflowStateBuilder.Create(repo.Path, stack: DotNetStack);
        state.Stage = WorkflowStage.Recovering;
        state.AppliedFiles.Add("SinglePageSample.Repository/TimesheetRepository.cs");
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

        IReadOnlyList<string> paths = MissingTestRecoverySupport.CollectRecoveryTestOverwritePaths(state, repo.Path);
        Assert.Contains(
            paths,
            path => path.EndsWith("TimesheetRepositoryTests.cs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TryValidateOverwrite_allows_path_from_prior_apply_rejection_in_compliance_issues()
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

        WorkflowState state = WorkflowStateBuilder.Create(repo.Path, stack: DotNetStack);
        state.Stage = WorkflowStage.Recovering;
        state.ComplianceIssues.Add(
            "Apply rejected 'SinglePageSample.UnitTest/RepositoryTest/TimesheetRepositoryTests.cs': "
            + "Recovery rejected overwrite");
        state.Audit = new AgentResult
        {
            Findings =
            [
                new AgentFinding
                {
                    Severity = FindingSeverity.High,
                    Message = "Missing unit tests"
                }
            ]
        };
        state.BuildValidation = new AgentResult { ProductionBuildPassed = true, TestsPassed = true, Findings = [] };

        Assert.True(RecoveryApplySupport.TryValidateDotNetRecoveryOverwrite(
            state,
            DotNetStack,
            repo.Path,
            "SinglePageSample.UnitTest/RepositoryTest/TimesheetRepositoryTests.cs",
            existedBefore: true,
            out _));
    }
}
