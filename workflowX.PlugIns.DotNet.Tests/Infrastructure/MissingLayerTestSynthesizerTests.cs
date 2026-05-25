using workflowX.Tests.Helpers;

namespace workflowX.PlugIns.DotNet.Tests.Infrastructure;

public class MissingLayerTestSynthesizerTests
{
    [Fact]
    public void GetRequiredTestPaths_includes_repository_and_controller_tests_from_architecture_deliverables()
    {
        using var repo = CreateSampleRepo();

        WorkflowState state = CreateState(repo.Path);
        state.ArchitecturePlan = new ArchitecturePlan
        {
            BackendFiles =
            [
                new ArchitectureDeliverable("SinglePageSample.Repository/Repositories/TimesheetRepository.cs"),
                new ArchitectureDeliverable("SinglePageSample.WebAPI/Controllers/TimesheetController.cs")
            ],
            TestStrategy = "Unit tests for TimesheetRepository and TimesheetController."
        };

        IReadOnlyList<string> requiredTests = MissingLayerTestSynthesizer.GetRequiredTestPaths(state);

        Assert.Contains("SinglePageSample.UnitTest/Repositories/TimesheetRepositoryTests.cs", requiredTests);
        Assert.Contains("SinglePageSample.UnitTest/Controllers/TimesheetControllerTests.cs", requiredTests);
    }

    [Fact]
    public void SynthesizeMissingTests_clones_repository_and_controller_tests_from_exemplars()
    {
        using var repo = CreateSampleRepo();
        repo.WriteFile(
            "SinglePageSample.Repository/Repositories/TimesheetRepository.cs",
            """
            namespace SinglePageSample.Repository.Repositories;

            public class TimesheetRepository
            {
            }
            """);
        repo.WriteFile(
            "SinglePageSample.WebAPI/Controllers/TimesheetController.cs",
            """
            namespace SinglePageSample.WebAPI.Controllers;

            public class TimesheetController
            {
            }
            """);

        WorkflowState state = CreateState(repo.Path);
        state.ArchitecturePlan = new ArchitecturePlan
        {
            BackendFiles =
            [
                new ArchitectureDeliverable("SinglePageSample.Repository/Repositories/TimesheetRepository.cs"),
                new ArchitectureDeliverable("SinglePageSample.WebAPI/Controllers/TimesheetController.cs")
            ]
        };
        state.AppliedFiles.Add("SinglePageSample.Repository/Repositories/TimesheetRepository.cs");
        state.AppliedFiles.Add("SinglePageSample.WebAPI/Controllers/TimesheetController.cs");

        IReadOnlyList<GeneratedFile> synthesized = MissingLayerTestSynthesizer.SynthesizeMissingTests(state);

        Assert.Equal(2, synthesized.Count);
        GeneratedFile repositoryTest = Assert.Single(synthesized, file =>
            file.RelativePath.EndsWith("TimesheetRepositoryTests.cs", StringComparison.OrdinalIgnoreCase));
        GeneratedFile controllerTest = Assert.Single(synthesized, file =>
            file.RelativePath.EndsWith("TimesheetControllerTests.cs", StringComparison.OrdinalIgnoreCase));

        Assert.Contains("class TimesheetRepositoryTests", repositoryTest.Content, StringComparison.Ordinal);
        Assert.Contains("class TimesheetControllerTests", controllerTest.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("EmployeeRepository", repositoryTest.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("EmployeeController", controllerTest.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void SynthesizeMissingTests_replaces_incomplete_llm_test_with_exemplar_clone()
    {
        using var repo = CreateSampleRepo();
        repo.WriteFile(
            "SinglePageSample.Repository/Repositories/TimesheetRepository.cs",
            """
            namespace SinglePageSample.Repository.Repositories;

            public class TimesheetRepository
            {
                public void Insert(Timesheet entity)
                {
                }
            }
            """);
        repo.WriteFile(
            "SinglePageSample.Repository/Entities/Timesheet.cs",
            """
            namespace SinglePageSample.Repository.Entities;

            public class Timesheet
            {
            }
            """);

        WorkflowState state = CreateState(repo.Path);
        state.ArchitecturePlan = new ArchitecturePlan
        {
            BackendFiles = [new ArchitectureDeliverable("SinglePageSample.Repository/Repositories/TimesheetRepository.cs")]
        };
        state.AppliedFiles.Add("SinglePageSample.Repository/Repositories/TimesheetRepository.cs");
        state.Backend = new AgentResult
        {
            ProposedFiles =
            [
                new GeneratedFile
                {
                    RelativePath = "SinglePageSample.UnitTest/Repositories/TimesheetRepositoryTests.cs",
                    Content = """
                        public class TimesheetRepositoryTests
                        {
                            private IEmployeeRepository _employeeRepository;

                            [TestInitialize]
                            public void Setup()
                            {
                                _employeeRepository = HotSpot.Resolve<IEmployeeRepository>();
                            }
                        }
                        """
                }
            ]
        };

        IReadOnlyList<GeneratedFile> synthesized = MissingLayerTestSynthesizer.SynthesizeMissingTests(state);

        GeneratedFile repositoryTest = Assert.Single(synthesized);
        Assert.Contains("HotSpot.Reset();", repositoryTest.Content, StringComparison.Ordinal);
        Assert.Contains("_timesheetRepository = HotSpot.Resolve<ITimesheetRepository>();", repositoryTest.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("_employeeRepository", repositoryTest.Content, StringComparison.Ordinal);
    }

    private static TempRepo CreateSampleRepo()
    {
        var repo = new TempRepo();
        repo.WriteFile(
            "SinglePageSample.Repository/Repositories/EmployeeRepository.cs",
            """
            namespace SinglePageSample.Repository.Repositories;

            public class EmployeeRepository
            {
            }
            """);
        repo.WriteFile(
            "SinglePageSample.WebAPI/Controllers/EmployeeController.cs",
            """
            namespace SinglePageSample.WebAPI.Controllers;

            public class EmployeeController
            {
            }
            """);
        repo.WriteFile(
            "SinglePageSample.UnitTest/Repositories/EmployeeRepositoryTests.cs",
            """
            namespace SinglePageSample.UnitTest.Repositories;

            public class EmployeeRepositoryTests
            {
                private IEmployeeRepository _employeeRepository;

                [TestInitialize]
                public void Setup()
                {
                    HotSpot.Reset();
                    _employeeRepository = HotSpot.Resolve<IEmployeeRepository>();
                }

                public void Insert_ShouldPersistEmployee()
                {
                    _employeeRepository.Insert(new Employee());
                }
            }
            """);
        repo.WriteFile(
            "SinglePageSample.UnitTest/Controllers/EmployeeControllerTests.cs",
            """
            namespace SinglePageSample.UnitTest.Controllers;

            public class EmployeeControllerTests
            {
                public void GetEmployees_works()
                {
                }
            }
            """);
        return repo;
    }

    private static WorkflowState CreateState(string repoPath) =>
        WorkflowStateBuilder.Create(repoPath, stack: new RepoStack(true, false));
}
