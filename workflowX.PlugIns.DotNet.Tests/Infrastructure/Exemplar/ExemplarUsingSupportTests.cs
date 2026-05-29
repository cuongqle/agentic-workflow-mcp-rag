using workflowX.Infrastructure;
using workflowX.Tests.Helpers;

namespace workflowX.PlugIns.DotNet.Tests.Infrastructure.Exemplar;

public class ExemplarUsingSupportTests
{
    [Fact]
    public void BuildImplementationUsingHints_includes_cross_role_using_for_companion_type()
    {
        using TempRepo repo = new();
        repo.WriteFile(
            "Sample.Project/Repositories/CompanyRepository.cs",
            """
            using Sample.Project.Indexes;

            namespace Sample.Project.Repositories;

            public class CompanyRepository
            {
                private readonly CompanyIndex _index;
            }
            """);
        repo.WriteFile(
            "Sample.Project/Indexes/CompanyIndex.cs",
            """
            namespace Sample.Project.Indexes;

            public class CompanyIndex
            {
            }
            """);

        IReadOnlyList<string> planned =
        [
            "Sample.Project/Repositories/TimesheetRepository.cs",
            "Sample.Project/Indexes/TimesheetIndex.cs"
        ];
        IReadOnlyList<string> productionPaths =
            ProductionPathExemplarSupport.DiscoverProductionRelativePaths(repo.Path);

        string hints = ExemplarUsingSupport.BuildImplementationUsingHints(repo.Path, planned, productionPaths);

        Assert.Contains("using Sample.Project.Indexes", hints, StringComparison.Ordinal);
        Assert.Contains("TimesheetRepository.cs", hints, StringComparison.Ordinal);
        Assert.Contains("TimesheetIndex", hints, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildContext_includes_required_usings_section()
    {
        using TempRepo repo = new();
        repo.WriteFile(
            "Sample.Project/Repositories/CompanyRepository.cs",
            """
            using Sample.Project.Indexes;

            public class CompanyRepository
            {
                private readonly CompanyIndex _index;
            }
            """);
        repo.WriteFile("Sample.Project/Indexes/CompanyIndex.cs", "namespace Sample.Project.Indexes; public class CompanyIndex { }");

        var state = new WorkflowState
        {
            RepoPath = repo.Path,
            ArchitecturePlan = new ArchitecturePlan
            {
                BackendFiles =
                [
                    new ArchitectureDeliverable("Sample.Project/Repositories/TimesheetRepository.cs", "impl"),
                    new ArchitectureDeliverable("Sample.Project/Indexes/TimesheetIndex.cs", "impl")
                ]
            }
        };

        string context = ImplementationExemplarSupport.BuildContext(state);

        Assert.Contains("Required usings and namespaces", context, StringComparison.Ordinal);
        Assert.Contains("using Sample.Project.Indexes", context, StringComparison.Ordinal);
    }
}
