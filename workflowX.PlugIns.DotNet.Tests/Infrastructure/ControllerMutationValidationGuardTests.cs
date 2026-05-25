using workflowX.Infrastructure;
using workflowX.Infrastructure.CodeApply.DotNet;

namespace workflowX.PlugIns.DotNet.Tests.Infrastructure;

public class ControllerMutationValidationGuardTests
{
    [Fact]
    public void TryValidate_fails_when_create_action_skips_foreign_key_lookup()
    {
        const string entityContent = """
            namespace Sample.Repository.Entities
            {
                public class Timesheet
                {
                    public int Id { get; set; }
                    public int EmployeeId { get; set; }
                }
            }
            """;

        const string controllerContent = """
            namespace Sample.WebAPI.Controllers
            {
                public class TimesheetController
                {
                    private readonly ITimesheetRepository _timesheetRepository;

                    [HttpPost]
                    public IActionResult Create(Timesheet timesheet)
                    {
                        _timesheetRepository.Create(timesheet);
                        return Ok();
                    }
                }
            }
            """;

        const string exemplarEntity = """
            namespace Sample.Repository.Entities
            {
                public class Employee
                {
                    public int Id { get; set; }
                    public int CompanyId { get; set; }
                }
            }
            """;

        const string exemplarController = """
            namespace Sample.WebAPI.Controllers
            {
                public class EmployeeController
                {
                    private readonly ICompanyRepository CompanyRepository;

                    [HttpPost]
                    public IActionResult PostEmployee(Employee employee)
                    {
                        var company = CompanyRepository.GetById(employee.CompanyId);
                        if (company == null)
                        {
                            return NotFound();
                        }

                        return Ok();
                    }
                }
            }
            """;

        string repoPath = CreateRepo(
            ("SinglePageSample.Repository/Entities/Employee.cs", exemplarEntity),
            ("SinglePageSample.WebAPI/Controllers/EmployeeController.cs", exemplarController));

        Assert.False(ControllerMutationValidationGuard.TryValidate(
            repoPath,
            "SinglePageSample.WebAPI/Controllers/TimesheetController.cs",
            controllerContent,
            entityContent,
            out string reason));
        Assert.Contains("EmployeeId", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void TryValidate_passes_when_create_validates_foreign_key_like_exemplar()
    {
        const string entityContent = """
            namespace Sample.Repository.Entities
            {
                public class Timesheet
                {
                    public int Id { get; set; }
                    public int EmployeeId { get; set; }
                }
            }
            """;

        const string controllerContent = """
            namespace Sample.WebAPI.Controllers
            {
                public class TimesheetController
                {
                    private readonly ITimesheetRepository _timesheetRepository;
                    private readonly IEmployeeRepository _employeeRepository;

                    [HttpPost]
                    public IActionResult Create(Timesheet timesheet)
                    {
                        var employee = _employeeRepository.GetById(timesheet.EmployeeId);
                        if (employee == null)
                        {
                            return NotFound();
                        }

                        _timesheetRepository.Create(timesheet);
                        return Ok();
                    }
                }
            }
            """;

        const string exemplarEntity = """
            namespace Sample.Repository.Entities
            {
                public class Employee
                {
                    public int Id { get; set; }
                    public int CompanyId { get; set; }
                }
            }
            """;

        const string exemplarController = """
            namespace Sample.WebAPI.Controllers
            {
                public class EmployeeController
                {
                    private readonly ICompanyRepository CompanyRepository;

                    [HttpPost]
                    public IActionResult PostEmployee(Employee employee)
                    {
                        var company = CompanyRepository.GetById(employee.CompanyId);
                        if (company == null)
                        {
                            return NotFound();
                        }

                        return Ok();
                    }
                }
            }
            """;

        string repoPath = CreateRepo(
            ("SinglePageSample.Repository/Entities/Employee.cs", exemplarEntity),
            ("SinglePageSample.WebAPI/Controllers/EmployeeController.cs", exemplarController));

        Assert.True(ControllerMutationValidationGuard.TryValidate(
            repoPath,
            "SinglePageSample.WebAPI/Controllers/TimesheetController.cs",
            controllerContent,
            entityContent,
            out string reason));
        Assert.Empty(reason);
    }

    [Fact]
    public void TryValidate_passes_when_exemplar_uses_non_GetById_lookup_member()
    {
        const string entityContent = """
            namespace Sample.Repository.Entities
            {
                public class Timesheet
                {
                    public int Id { get; set; }
                    public int EmployeeId { get; set; }
                }
            }
            """;

        const string controllerContent = """
            namespace Sample.WebAPI.Controllers
            {
                public class TimesheetController
                {
                    private readonly IEmployeeRepository _employeeRepository;
                    private readonly ITimesheetRepository _timesheetRepository;

                    [HttpPost]
                    public IActionResult Create(Timesheet timesheet)
                    {
                        var employee = _employeeRepository.Load(timesheet.EmployeeId);
                        if (employee == null)
                        {
                            return NotFound();
                        }

                        _timesheetRepository.Create(timesheet);
                        return Ok();
                    }
                }
            }
            """;

        const string exemplarEntity = """
            namespace Sample.Repository.Entities
            {
                public class Employee
                {
                    public int Id { get; set; }
                    public int CompanyId { get; set; }
                }
            }
            """;

        const string exemplarController = """
            namespace Sample.WebAPI.Controllers
            {
                public class EmployeeController
                {
                    private readonly ICompanyRepository CompanyRepository;

                    [HttpPost]
                    public IActionResult PostEmployee(Employee employee)
                    {
                        var company = CompanyRepository.Load(employee.CompanyId);
                        if (company == null)
                        {
                            return NotFound();
                        }

                        return Ok();
                    }
                }
            }
            """;

        string repoPath = CreateRepo(
            ("SinglePageSample.Repository/Entities/Employee.cs", exemplarEntity),
            ("SinglePageSample.WebAPI/Controllers/EmployeeController.cs", exemplarController));

        Assert.True(ControllerMutationValidationGuard.TryValidate(
            repoPath,
            "SinglePageSample.WebAPI/Controllers/TimesheetController.cs",
            controllerContent,
            entityContent,
            out string reason));
        Assert.Empty(reason);
    }

    private static string CreateRepo(params (string RelativePath, string Content)[] files)
    {
        string repoPath = Path.Combine(Path.GetTempPath(), "workflowx-mutation-guard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(repoPath);
        foreach ((string relativePath, string content) in files)
        {
            string absolute = Path.Combine(repoPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
            File.WriteAllText(absolute, content);
        }

        return repoPath;
    }
}
