using workflowX.Infrastructure;
using workflowX.Tests.Helpers;

namespace workflowX.PlugIns.DotNet.Tests.Infrastructure.Exemplar;

public class ExemplarRoleCompanionSupportTests
{
    [Fact]
    public void DiscoverMissingCompanionPaths_adds_index_when_repository_exemplar_references_it()
    {
        using TempRepo repo = new();
        repo.WriteFile(
            "Sample.Project/Repositories/CompanyRepository.cs",
            """
            public class CompanyRepository
            {
                private readonly CompanyIndex _index;
            }
            """);
        repo.WriteFile(
            "Sample.Project/Indexes/CompanyIndex.cs",
            """
            public class CompanyIndex
            {
            }
            """);

        IReadOnlyList<string> planned =
        [
            "Sample.Project/Repositories/TimesheetRepository.cs"
        ];

        IReadOnlyList<string> missing = ExemplarRoleCompanionSupport.DiscoverMissingCompanionPaths(repo.Path, planned);

        Assert.Contains(
            missing,
            path => path.Equals("Sample.Project/Indexes/TimesheetIndex.cs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DiscoverCompanionExemplarPaths_returns_on_disk_index_exemplar()
    {
        using TempRepo repo = new();
        repo.WriteFile(
            "Sample.Project/Repositories/CompanyRepository.cs",
            """
            public class CompanyRepository
            {
                private readonly CompanyIndex _index;
            }
            """);
        repo.WriteFile("Sample.Project/Indexes/CompanyIndex.cs", "public class CompanyIndex { }");

        IReadOnlyList<string> productionPaths =
            ProductionPathExemplarSupport.DiscoverProductionRelativePaths(repo.Path);
        IReadOnlyList<string> companions = ExemplarRoleCompanionSupport.DiscoverCompanionExemplarPaths(
            repo.Path,
            "Sample.Project/Repositories/TimesheetRepository.cs",
            productionPaths);

        Assert.Contains("Sample.Project/Indexes/CompanyIndex.cs", companions, StringComparer.OrdinalIgnoreCase);
    }
}
