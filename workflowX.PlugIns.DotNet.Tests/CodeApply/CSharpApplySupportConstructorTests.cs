using workflowX.Infrastructure;
using workflowX.Tests.Helpers;

namespace workflowX.PlugIns.DotNet.Tests.CodeApply;

public class CSharpApplySupportConstructorTests
{
    [Fact]
    public async Task ApplyAsync_passes_through_controller_content_without_managed_mutations()
    {
        using var repo = new TempRepo();
        WriteDotNetProject(repo);
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

        const string proposedController = """
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
            """;

        WorkflowState state = CreateApplyState(repo.Path);
        state.Backend = new AgentResult
        {
            ProposedFiles =
            [
                new GeneratedFile
                {
                    RelativePath = "SinglePageSample.WebAPI/Controllers/TimesheetController.cs",
                    Content = proposedController
                }
            ]
        };

        ApplyResult result = await GeneratedFileApplier.ApplyAsync(state);

        Assert.Empty(result.RejectedFiles);
        Assert.Single(result.AppliedFiles);
        string written = File.ReadAllText(
            Path.Combine(repo.Path, "SinglePageSample.WebAPI/Controllers/TimesheetController.cs"));
        Assert.Equal(proposedController.Replace("\r\n", "\n"), written.Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task ApplyAsync_passes_through_repository_call_without_rewriting_add_to_insert()
    {
        using var repo = new TempRepo();
        WriteDotNetProject(repo);
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

        const string proposedController = """
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
            """;

        WorkflowState state = CreateApplyState(repo.Path);
        state.Backend = new AgentResult
        {
            ProposedFiles =
            [
                new GeneratedFile
                {
                    RelativePath = "SinglePageSample.WebAPI/Controllers/TimesheetController.cs",
                    Content = proposedController
                }
            ]
        };

        ApplyResult result = await GeneratedFileApplier.ApplyAsync(state);

        Assert.Empty(result.RejectedFiles);
        string written = File.ReadAllText(
            Path.Combine(repo.Path, "SinglePageSample.WebAPI/Controllers/TimesheetController.cs"));
        Assert.Contains("_repository.Add(value);", written, StringComparison.Ordinal);
        Assert.DoesNotContain("_repository.Insert(value);", written, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyAsync_passes_through_partial_constructor_assignments()
    {
        using var repo = new TempRepo();
        WriteDotNetProject(repo);
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

        const string proposedController = """
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
            """;

        WorkflowState state = CreateApplyState(repo.Path);
        state.Backend = new AgentResult
        {
            ProposedFiles =
            [
                new GeneratedFile
                {
                    RelativePath = "SinglePageSample.WebAPI/Controllers/TimesheetController.cs",
                    Content = proposedController
                }
            ]
        };

        ApplyResult result = await GeneratedFileApplier.ApplyAsync(state);

        Assert.Empty(result.RejectedFiles);
        string written = File.ReadAllText(
            Path.Combine(repo.Path, "SinglePageSample.WebAPI/Controllers/TimesheetController.cs"));
        Assert.Contains("TimesheetRepository = timesheetRepository;", written, StringComparison.Ordinal);
        Assert.DoesNotContain("this.EmployeeRepository = employeeRepository", written, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyAsync_accepts_post_timesheet_with_frombody_addasync()
    {
        using var repo = new TempRepo();
        WriteDotNetProject(repo);
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

        WorkflowState state = CreateApplyState(repo.Path);
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

    private static WorkflowState CreateApplyState(string repoPath)
    {
        WorkflowState state = WorkflowStateBuilder.Create(repoPath, stack: new RepoStack(true, false));
        state.Contract = RepoContractDiscoverer.Discover(repoPath);
        state.Stage = WorkflowStage.Implementing;
        return state;
    }

    private static void WriteDotNetProject(TempRepo repo) =>
        repo.WriteFile("SinglePageSample.WebAPI/SinglePageSample.WebAPI.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
}
