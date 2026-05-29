using workflowX.Infrastructure;
using workflowX.Tests.Helpers;

namespace workflowX.PlugIns.DotNet.Tests.Infrastructure.Exemplar;

public class ImplementationExemplarSupportTests
{
    [Fact]
    public void BuildContext_includes_same_kind_exemplar_full_source()
    {
        using TempRepo repo = new();
        repo.WriteFile(
            "Sample.Project/CompanyRepository.cs",
            """
            public class CompanyRepository
            {
                public Task SaveAsync(Company item) => _store.InsertAsync(item);
            }
            """);

        var state = new WorkflowState
        {
            RepoPath = repo.Path,
            ArchitecturePlan = new ArchitecturePlan
            {
                BackendFiles =
                [
                    new ArchitectureDeliverable("Sample.Project/TimesheetRepository.cs", "impl")
                ]
            }
        };

        string context = ImplementationExemplarSupport.BuildContext(state);

        Assert.Contains("CompanyRepository.cs", context, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("InsertAsync", context, StringComparison.Ordinal);
        Assert.DoesNotContain("TimesheetRepository.cs", context, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildContext_includes_same_folder_exemplar_when_planned_file_has_no_role_suffix()
    {
        using TempRepo repo = new();
        repo.WriteFile(
            "Sample.Project/Entities/Company.cs",
            """
            using Sample.Domain;

            public class Company : IEntity
            {
            }
            """);

        var state = new WorkflowState
        {
            RepoPath = repo.Path,
            ArchitecturePlan = new ArchitecturePlan
            {
                BackendFiles =
                [
                    new ArchitectureDeliverable("Sample.Project/Entities/Timesheet.cs", "impl")
                ]
            }
        };

        string context = ImplementationExemplarSupport.BuildContext(state);

        Assert.Contains("Company.cs", context, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("using Sample.Domain", context, StringComparison.Ordinal);
        Assert.DoesNotContain("Timesheet.cs", context, StringComparison.OrdinalIgnoreCase);
    }
}
