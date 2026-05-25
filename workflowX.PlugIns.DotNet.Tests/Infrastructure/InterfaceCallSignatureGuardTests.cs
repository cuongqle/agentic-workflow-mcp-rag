using workflowX.Infrastructure.CodeApply.DotNet;

namespace workflowX.PlugIns.DotNet.Tests.Infrastructure;

public class InterfaceCallSignatureGuardTests
{
    [Fact]
    public void TryValidate_fails_when_controller_passes_string_to_int_repository_parameter()
    {
        const string interfaceContent = """
            namespace Sample.Repository.Interfaces
            {
                public interface ITimesheetRepository
                {
                    System.Threading.Tasks.Task<System.Collections.Generic.IEnumerable<object>> GetTimesheetsByEmployeeIdAsync(int employeeId);
                }
            }
            """;

        const string controllerContent = """
            namespace Sample.WebAPI.Controllers
            {
                public class TimesheetController
                {
                    private readonly ITimesheetRepository _timesheetRepository;

                    public TimesheetController(ITimesheetRepository timesheetRepository)
                    {
                        _timesheetRepository = timesheetRepository;
                    }

                    public async System.Threading.Tasks.Task GetTimesheetsByEmployeeId(string employeeId)
                    {
                        var timesheets = await _timesheetRepository.GetTimesheetsByEmployeeIdAsync(employeeId);
                    }
                }
            }
            """;

        var catalog = InterfaceCallSignatureGuard.BuildCatalog(
            repoPath: string.Empty,
            proposedFiles:
            [
                new GeneratedFile { RelativePath = "ITimesheetRepository.cs", Content = interfaceContent }
            ]);

        Assert.False(InterfaceCallSignatureGuard.TryValidate(controllerContent, catalog, out string reason));
        Assert.Contains("string", reason, StringComparison.Ordinal);
        Assert.Contains("int", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void TryValidate_passes_when_controller_matches_repository_parameter_types()
    {
        const string interfaceContent = """
            namespace Sample.Repository.Interfaces
            {
                public interface ITimesheetRepository
                {
                    System.Threading.Tasks.Task<System.Collections.Generic.IEnumerable<object>> GetTimesheetsByEmployeeIdAsync(int employeeId);
                }
            }
            """;

        const string controllerContent = """
            namespace Sample.WebAPI.Controllers
            {
                public class TimesheetController
                {
                    private readonly ITimesheetRepository _timesheetRepository;

                    public async System.Threading.Tasks.Task GetTimesheetsByEmployeeId(int employeeId)
                    {
                        var timesheets = await _timesheetRepository.GetTimesheetsByEmployeeIdAsync(employeeId);
                    }
                }
            }
            """;

        var catalog = InterfaceCallSignatureGuard.BuildCatalog(
            repoPath: string.Empty,
            proposedFiles:
            [
                new GeneratedFile { RelativePath = "ITimesheetRepository.cs", Content = interfaceContent }
            ]);

        Assert.True(InterfaceCallSignatureGuard.TryValidate(controllerContent, catalog, out string reason));
        Assert.Empty(reason);
    }

    [Fact]
    public void TryValidate_allows_declared_role_interface_Update_calls()
    {
        const string interfaceContent = """
            namespace Sample.Repository.Interfaces
            {
                public interface ITimesheetRepository
                {
                    void Update(Sample.Repository.Entities.Timesheet timesheet);
                }
            }
            """;

        const string controllerContent = """
            namespace Sample.WebAPI.Controllers
            {
                public class TimesheetController
                {
                    private readonly ITimesheetRepository _timesheetRepository;

                    public void Put(Sample.Repository.Entities.Timesheet timesheet)
                    {
                        _timesheetRepository.Update(timesheet);
                    }
                }
            }
            """;

        var catalog = InterfaceCallSignatureGuard.BuildCatalog(
            repoPath: string.Empty,
            proposedFiles:
            [
                new GeneratedFile { RelativePath = "ITimesheetRepository.cs", Content = interfaceContent }
            ]);

        Assert.True(InterfaceCallSignatureGuard.TryValidate(controllerContent, catalog, out string reason));
        Assert.Empty(reason);
    }

    [Fact]
    public void TryValidate_rejects_undeclared_infrastructure_method_calls()
    {
        const string storeContent = """
            namespace Sample.Db.DbStore
            {
                public interface IDbStore
                {
                    void Save<T>(T entity);
                }
            }
            """;

        const string repositoryContent = """
            namespace Sample.Repository
            {
                public class TimesheetRepository
                {
                    private readonly IDbStore _dbStore;

                    public void Save(Sample.Repository.Entities.Timesheet timesheet)
                    {
                        _dbStore.Update(timesheet);
                    }
                }
            }
            """;

        var catalog = InterfaceCallSignatureGuard.BuildCatalog(
            repoPath: string.Empty,
            proposedFiles:
            [
                new GeneratedFile { RelativePath = "IDbStore.cs", Content = storeContent }
            ]);

        Assert.False(InterfaceCallSignatureGuard.TryValidate(repositoryContent, catalog, out string reason));
        Assert.Contains("not declared on IDbStore", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void TryValidate_passes_when_frombody_action_parameter_is_passed_to_repository()
    {
        const string interfaceContent = """
            namespace SinglePageSample.Repository.Interfaces
            {
                public interface ITimesheetRepository
                {
                    System.Threading.Tasks.Task AddAsync(SinglePageSample.Repository.Entities.Timesheet timesheet);
                }
            }
            """;

        const string controllerContent = """
            namespace SinglePageSample.WebAPI.Controllers
            {
                public class TimesheetController
                {
                    private readonly ITimesheetRepository _timesheetRepository;

                    public async System.Threading.Tasks.Task PostTimesheet([FromBody] SinglePageSample.Repository.Entities.Timesheet timesheet)
                    {
                        await _timesheetRepository.AddAsync(timesheet);
                    }
                }
            }
            """;

        var catalog = InterfaceCallSignatureGuard.BuildCatalog(
            repoPath: string.Empty,
            proposedFiles:
            [
                new GeneratedFile { RelativePath = "ITimesheetRepository.cs", Content = interfaceContent }
            ]);

        Assert.True(InterfaceCallSignatureGuard.TryValidate(controllerContent, catalog, out string reason), reason);
    }

    [Fact]
    public void TryValidate_passes_insert_inherited_from_irepository_on_role_interface()
    {
        const string repositoryInterfaces = """
            namespace Sample.Repository.Interfaces;

            public interface IRepository<T> where T : class
            {
                void Insert(T entity);
                void Delete(T entity);
            }

            public interface IEmployeeRepository : IRepository<Employee>
            {
                Employee GetById(int id);
            }
            """;

        const string testContent = """
            namespace Sample.UnitTest;

            public class TimesheetRepositoryTests
            {
                private readonly IEmployeeRepository _employeeRepository;

                public void CanSeedEmployee(Employee employee)
                {
                    _employeeRepository.Insert(employee);
                }
            }
            """;

        var catalog = InterfaceCallSignatureGuard.BuildCatalog(
            repoPath: string.Empty,
            proposedFiles:
            [
                new GeneratedFile { RelativePath = "IEmployeeRepository.cs", Content = repositoryInterfaces }
            ]);

        Assert.True(InterfaceCallSignatureGuard.TryValidate(testContent, catalog, out string reason), reason);
    }

    [Fact]
    public void TryValidate_still_rejects_invented_async_methods_on_role_interface()
    {
        const string repositoryInterfaces = """
            namespace Sample.Repository.Interfaces;

            public interface IRepository<T> where T : class
            {
                void Insert(T entity);
            }

            public interface IEmployeeRepository : IRepository<Employee>
            {
                Employee GetById(int id);
            }
            """;

        const string testContent = """
            namespace Sample.UnitTest;

            public class TimesheetRepositoryTests
            {
                private readonly IEmployeeRepository _employeeRepository;

                public async System.Threading.Tasks.Task CanSeedEmployee(Employee employee)
                {
                    await _employeeRepository.AddAsync(employee);
                }
            }
            """;

        var catalog = InterfaceCallSignatureGuard.BuildCatalog(
            repoPath: string.Empty,
            proposedFiles:
            [
                new GeneratedFile { RelativePath = "IEmployeeRepository.cs", Content = repositoryInterfaces }
            ]);

        Assert.False(InterfaceCallSignatureGuard.TryValidate(testContent, catalog, out string reason));
        Assert.Contains("AddAsync", reason, StringComparison.Ordinal);
        Assert.Contains("Insert", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildRagContext_lists_inherited_members_on_derived_interfaces()
    {
        const string repositoryInterfaces = """
            namespace Sample.Repository.Interfaces;

            public interface IRepository<T> where T : class
            {
                void Insert(T entity);
            }

            public interface IEmployeeRepository : IRepository<Employee>
            {
                Employee GetById(int id);
            }
            """;

        string? context = InterfaceCallSignatureGuard.BuildRagContext(
            repoPath: string.Empty,
            proposedFiles:
            [
                new GeneratedFile { RelativePath = "IEmployeeRepository.cs", Content = repositoryInterfaces }
            ]);

        Assert.NotNull(context);
        Assert.Contains("IEmployeeRepository", context, StringComparison.Ordinal);
        Assert.Contains("Insert", context, StringComparison.Ordinal);
    }

    [Fact]
    public void PreExistingContractGuard_refuses_adding_members_to_existing_interface()
    {
        const string existing = """
            namespace Sample.Repository.Interfaces;

            public interface IEmployeeRepository
            {
                void Insert(object employee);
            }
            """;

        const string proposed = """
            namespace Sample.Repository.Interfaces;

            public interface IEmployeeRepository
            {
                void Insert(object employee);
                bool ExistAsync(int id);
            }
            """;

        var proposedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Sample.Repository/Interfaces/IEmployeeRepository.cs"
        };

        bool valid = PreExistingContractGuard.TryValidateOverwrite(
            "Sample.Repository/Interfaces/IEmployeeRepository.cs",
            existing,
            proposed,
            proposedPaths,
            repoPath: string.Empty,
            out string reason);

        Assert.False(valid);
        Assert.Contains("IEmployeeRepository", reason, StringComparison.Ordinal);
    }
}
