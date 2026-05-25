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
}
