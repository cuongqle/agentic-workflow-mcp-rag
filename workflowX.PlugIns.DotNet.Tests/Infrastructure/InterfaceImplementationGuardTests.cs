using workflowX.Infrastructure.CodeApply.DotNet;
using workflowX.Tests.Helpers;

namespace workflowX.PlugIns.DotNet.Tests.Infrastructure;

public class InterfaceImplementationGuardTests
{
    [Fact]
    public void TryValidate_fails_when_implementation_method_parameter_types_do_not_match_interface()
    {
        using var repo = new TempRepo();
        repo.WriteFile(
            "SinglePageSample/SinglePageSample.Repository/Interfaces/ITimesheetRepository.cs",
            """
            namespace SinglePageSample.Repository.Interfaces;

            public interface ITimesheetRepository
            {
                Timesheet GetById(Guid id);
                void Delete(Guid id);
            }
            """);

        const string implementation = """
            namespace SinglePageSample.Repository;

            public class TimesheetRepository : ITimesheetRepository
            {
                public Timesheet GetById(int id)
                {
                    return default!;
                }

                public void Delete(int id)
                {
                }
            }
            """;

        var directMembers = InterfaceImplementationGuard.BuildDirectMemberCatalog(repo.Path, Array.Empty<GeneratedFile>());
        bool valid = InterfaceImplementationGuard.TryValidate(
            repo.Path,
            "SinglePageSample/SinglePageSample.Repository/TimesheetRepository.cs",
            implementation,
            directMembers,
            out string reason);

        Assert.False(valid);
        Assert.Contains("signature mismatch", reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GetById", reason, StringComparison.Ordinal);
    }
}
