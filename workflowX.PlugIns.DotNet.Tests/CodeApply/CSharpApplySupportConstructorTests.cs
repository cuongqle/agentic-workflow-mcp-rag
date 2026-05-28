using workflowX.Infrastructure;
using workflowX.Tests.Helpers;

namespace workflowX.PlugIns.DotNet.Tests.CodeApply;

public class CSharpApplySupportConstructorTests
{
    [Fact]
    public async Task ApplyAsync_injects_shared_constructor_dependency_from_controller_exemplar()
    {
        using var repo = new TempRepo();
        repo.WriteFile(
            "SinglePageSample.WebAPI/Controllers/EmployeeController.cs",
            """
            namespace SinglePageSample.WebAPI.Controllers;

            public class EmployeeController
            {
                public EmployeeController(IEmployeeRepository employeeRepository, ICompanyRepository companyRepository)
                {
                }
            }
            """);
        repo.WriteFile(
            "SinglePageSample.WebAPI/Controllers/CompanyController.cs",
            """
            namespace SinglePageSample.WebAPI.Controllers;

            public class CompanyController
            {
                public CompanyController(ICompanyRepository companyRepository)
                {
                }
            }
            """);

        WorkflowState state = WorkflowStateBuilder.Create(repo.Path, stack: new RepoStack(true, false));
        state.Contract = RepoContractDiscoverer.Discover(repo.Path);
        state.Stage = WorkflowStage.Implementing;
        state.Backend = new AgentResult
        {
            ProposedFiles =
            [
                new GeneratedFile
                {
                    RelativePath = "SinglePageSample.WebAPI/Controllers/TimesheetController.cs",
                    Content = """
                        namespace SinglePageSample.WebAPI.Controllers;

                        public class TimesheetController
                        {
                            private readonly ITimesheetRepository TimesheetRepository;

                            public TimesheetController(ITimesheetRepository timesheetRepository)
                            {
                                TimesheetRepository = timesheetRepository;
                            }

                            public void GetAllTimesheets()
                            {
                                _ = this.CompanyRepository;
                            }
                        }
                        """
                }
            ]
        };

        ApplyResult result = await GeneratedFileApplier.ApplyAsync(state);

        Assert.Empty(result.RejectedFiles);
        Assert.Single(result.AppliedFiles);
        string written = File.ReadAllText(
            Path.Combine(repo.Path, "SinglePageSample.WebAPI/Controllers/TimesheetController.cs"));
        Assert.Contains("ICompanyRepository companyRepository", written, StringComparison.Ordinal);
        Assert.Contains("private readonly ICompanyRepository CompanyRepository", written, StringComparison.Ordinal);
        Assert.Contains("CompanyRepository = companyRepository", written, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyAsync_rewrites_repository_add_call_to_insert_when_interface_declares_insert_only()
    {
        using var repo = new TempRepo();
        repo.WriteFile(
            "SinglePageSample.Repository/Interfaces/ITimesheetRepository.cs",
            """
            namespace SinglePageSample.Repository.Interfaces;

            public interface ITimesheetRepository
            {
                void Insert(Timesheet value);
            }
            """);
        repo.WriteFile(
            "SinglePageSample.Repository/Timesheet.cs",
            """
            namespace SinglePageSample.Repository;

            public class Timesheet { }
            """);
        repo.WriteFile(
            "SinglePageSample.WebAPI/TestBootstrapper.cs",
            """
            using Microsoft.Extensions.DependencyInjection;

            namespace SinglePageSample.WebAPI;

            public static class TestBootstrapper
            {
                public static IServiceProvider Create()
                {
                    var services = new ServiceCollection();
                    return services.BuildServiceProvider();
                }
            }
            """);

        WorkflowState state = WorkflowStateBuilder.Create(repo.Path, stack: new RepoStack(true, false));
        state.Contract = RepoContractDiscoverer.Discover(repo.Path);
        state.Stage = WorkflowStage.Implementing;
        state.Backend = new AgentResult
        {
            ProposedFiles =
            [
                new GeneratedFile
                {
                    RelativePath = "SinglePageSample.WebAPI/Controllers/TimesheetController.cs",
                    Content = """
                        namespace SinglePageSample.WebAPI.Controllers;

                        using SinglePageSample.Repository.Interfaces;

                        public class TimesheetController
                        {
                            private readonly ITimesheetRepository _repository;

                            public TimesheetController(ITimesheetRepository repository)
                            {
                                _repository = repository;
                            }

                            public void Save(Timesheet value)
                            {
                                _repository.Add(value);
                            }
                        }
                        """
                }
            ]
        };

        ApplyResult result = await GeneratedFileApplier.ApplyAsync(state);

        Assert.Empty(result.RejectedFiles);
        string written = File.ReadAllText(
            Path.Combine(repo.Path, "SinglePageSample.WebAPI/Controllers/TimesheetController.cs"));
        Assert.Contains("_repository.Insert(value);", written, StringComparison.Ordinal);
        Assert.DoesNotContain("_repository.Add(value);", written, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyAsync_injects_missing_constructor_field_for_this_member_access()
    {
        using var repo = new TempRepo();
        repo.WriteFile(
            "SinglePageSample.WebAPI/Controllers/EmployeeController.cs",
            """
            namespace SinglePageSample.WebAPI.Controllers;

            public class EmployeeController
            {
                private readonly IEmployeeRepository EmployeeRepository;
                private readonly ICompanyRepository CompanyRepository;

                public EmployeeController(IEmployeeRepository employeeRepository, ICompanyRepository companyRepository)
                {
                    this.EmployeeRepository = employeeRepository;
                    this.CompanyRepository = companyRepository;
                }

                public void PostEmployee()
                {
                    _ = this.CompanyRepository.GetById(1);
                }
            }
            """);
        repo.WriteFile(
            "SinglePageSample.WebAPI/Controllers/CompanyController.cs",
            """
            namespace SinglePageSample.WebAPI.Controllers;

            public class CompanyController
            {
                public CompanyController(ICompanyRepository companyRepository)
                {
                }
            }
            """);
        repo.WriteFile(
            "SinglePageSample.Repository/Interfaces/IEmployeeRepository.cs",
            """
            namespace SinglePageSample.Repository.Interfaces;

            public interface IEmployeeRepository
            {
                object GetById(int id);
            }
            """);
        repo.WriteFile(
            "SinglePageSample.Repository/Interfaces/ICompanyRepository.cs",
            """
            namespace SinglePageSample.Repository.Interfaces;

            public interface ICompanyRepository
            {
                object GetById(int id);
            }
            """);
        repo.WriteFile(
            "SinglePageSample.Repository/Interfaces/ITimesheetRepository.cs",
            """
            namespace SinglePageSample.Repository.Interfaces;

            public interface ITimesheetRepository
            {
                void Insert(object timesheet);
            }
            """);

        WorkflowState state = WorkflowStateBuilder.Create(repo.Path, stack: new RepoStack(true, false));
        state.Contract = RepoContractDiscoverer.Discover(repo.Path);
        state.Stage = WorkflowStage.Implementing;
        state.Backend = new AgentResult
        {
            ProposedFiles =
            [
                new GeneratedFile
                {
                    RelativePath = "SinglePageSample.WebAPI/Controllers/TimesheetController.cs",
                    Content = """
                        using Microsoft.AspNetCore.Mvc;
                        namespace SinglePageSample.WebAPI.Controllers
                        {
                            public class TimesheetController
                            {
                                private readonly ITimesheetRepository TimesheetRepository;

                                public TimesheetController(
                                    ITimesheetRepository timesheetRepository,
                                    IEmployeeRepository employeeRepository,
                                    ICompanyRepository companyRepository)
                                {
                                    TimesheetRepository = timesheetRepository;
                                }

                                public IActionResult PostTimesheet()
                                {
                                    var employee = this.EmployeeRepository.GetById(1);
                                    var company = this.CompanyRepository.GetById(1);
                                    return Ok();
                                }
                            }
                        }
                        """
                }
            ]
        };

        ApplyResult result = await GeneratedFileApplier.ApplyAsync(state);

        Assert.Empty(result.RejectedFiles);
        string written = File.ReadAllText(
            Path.Combine(repo.Path, "SinglePageSample.WebAPI/Controllers/TimesheetController.cs"));
        Assert.Contains("private readonly IEmployeeRepository EmployeeRepository", written, StringComparison.Ordinal);
        Assert.Contains("this.EmployeeRepository = employeeRepository", written, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyAsync_accepts_post_timesheet_with_frombody_addasync()
    {
        using var repo = new TempRepo();
        repo.WriteFile(
            "SinglePageSample.WebAPI/Controllers/EmployeeController.cs",
            """
            namespace SinglePageSample.WebAPI.Controllers;
            public class EmployeeController
            {
                public EmployeeController(IEmployeeRepository employeeRepository, ICompanyRepository companyRepository) { }
            }
            """);
        repo.WriteFile(
            "SinglePageSample.WebAPI/Controllers/CompanyController.cs",
            """
            namespace SinglePageSample.WebAPI.Controllers;
            public class CompanyController
            {
                public CompanyController(ICompanyRepository companyRepository) { }
            }
            """);
        repo.WriteFile(
            "SinglePageSample.Repository/Interfaces/IEmployeeRepository.cs",
            """
            namespace SinglePageSample.Repository.Interfaces;
            public interface IEmployeeRepository { object GetById(int id); }
            """);
        repo.WriteFile(
            "SinglePageSample.Repository/Interfaces/ICompanyRepository.cs",
            """
            namespace SinglePageSample.Repository.Interfaces;
            public interface ICompanyRepository { }
            """);
        repo.WriteFile(
            "SinglePageSample.Repository/Interfaces/ITimesheetRepository.cs",
            """
            using SinglePageSample.Repository.Entities;
            namespace SinglePageSample.Repository.Interfaces;
            public interface ITimesheetRepository
            {
                System.Threading.Tasks.Task AddAsync(Timesheet timesheet);
            }
            """);
        repo.WriteFile(
            "SinglePageSample.Repository/Entities/Timesheet.cs",
            """
            namespace SinglePageSample.Repository.Entities;
            public class Timesheet { public int EmployeeId { get; set; } }
            """);

        WorkflowState state = WorkflowStateBuilder.Create(repo.Path, stack: new RepoStack(true, false));
        state.Contract = RepoContractDiscoverer.Discover(repo.Path);
        state.Stage = WorkflowStage.Recovering;
        state.Recovery = new AgentResult
        {
            ProposedFiles =
            [
                new GeneratedFile
                {
                    RelativePath = "SinglePageSample.WebAPI/Controllers/TimesheetController.cs",
                    Content = """
                        using Microsoft.AspNetCore.Mvc;
                        using SinglePageSample.Repository.Entities;
                        using SinglePageSample.Repository.Interfaces;
                        using System.Threading.Tasks;

                        namespace SinglePageSample.WebAPI.Controllers
                        {
                            [ApiController]
                            public class TimesheetController : ControllerBase
                            {
                                private readonly ITimesheetRepository _timesheetRepository;
                                private readonly IEmployeeRepository _employeeRepository;
                                private readonly ICompanyRepository _companyRepository;

                                public TimesheetController(
                                    ITimesheetRepository timesheetRepository,
                                    IEmployeeRepository employeeRepository,
                                    ICompanyRepository companyRepository)
                                {
                                    _timesheetRepository = timesheetRepository;
                                    _employeeRepository = employeeRepository;
                                    _companyRepository = companyRepository;
                                }

                                [HttpPost]
                                public async Task<IActionResult> PostTimesheet([FromBody] Timesheet timesheet)
                                {
                                    if (_employeeRepository.GetById(timesheet.EmployeeId) is null)
                                    {
                                        return NotFound();
                                    }

                                    await _timesheetRepository.AddAsync(timesheet);
                                    return Ok();
                                }
                            }
                        }
                        """
                }
            ]
        };

        ApplyResult result = await GeneratedFileApplier.ApplyAsync(state);

        Assert.Empty(result.RejectedFiles);
        Assert.True(File.Exists(Path.Combine(
            repo.Path,
            "SinglePageSample.WebAPI/Controllers/TimesheetController.cs")));
    }
}
