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
}
