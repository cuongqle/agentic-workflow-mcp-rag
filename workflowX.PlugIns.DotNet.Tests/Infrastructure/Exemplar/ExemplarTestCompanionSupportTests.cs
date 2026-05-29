using workflowX.Infrastructure;
using workflowX.Tests.Helpers;

namespace workflowX.PlugIns.DotNet.Tests.Infrastructure.Exemplar;

public class ExemplarTestCompanionSupportTests
{
    [Fact]
    public void DiscoverMissingTestPaths_uses_exemplar_test_naming_and_folder()
    {
        using TempRepo repo = new();
        repo.WriteFile(
            "Acme.Repository/Repositories/CompanyRepository.cs",
            "namespace Acme.Repository; public class CompanyRepository { }");
        repo.WriteFile(
            "Acme.Tests/RepositoryTest/CompanyRepositoryTests.cs",
            "namespace Acme.Tests; public class CompanyRepositoryTests { }");

        IReadOnlyList<string> planned =
        [
            "Acme.Repository/Repositories/TimesheetRepository.cs"
        ];

        IReadOnlyList<string> missing = ExemplarTestCompanionSupport.DiscoverMissingTestPaths(repo.Path, planned);

        Assert.Contains(
            missing,
            path => path.Equals("Acme.Tests/RepositoryTest/TimesheetRepositoryTests.cs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DiscoverExpectedTestPath_matches_exemplar_pair_not_generic_subjectTests()
    {
        using TempRepo repo = new();
        repo.WriteFile("Acme.Repository/Repositories/CompanyRepository.cs", "class CompanyRepository { }");
        repo.WriteFile("Acme.Tests/RepositoryTest/CompanyRepositoryTests.cs", "class CompanyRepositoryTests { }");
        repo.WriteFile("Acme.Tests/EmployeeTests.cs", "class EmployeeTests { }");

        string? expected = ExemplarTestCompanionSupport.DiscoverExpectedTestPath(
            repo.Path,
            "Acme.Repository/Repositories/TimesheetRepository.cs");

        Assert.Equal("Acme.Tests/RepositoryTest/TimesheetRepositoryTests.cs", expected, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidatePlannedProductionTests_flags_missing_test_on_plan()
    {
        using TempRepo repo = new();
        repo.WriteFile("Acme.Repository/Repositories/CompanyRepository.cs", "class CompanyRepository { }");
        repo.WriteFile("Acme.Tests/RepositoryTest/CompanyRepositoryTests.cs", "class CompanyRepositoryTests { }");

        var plan = new ArchitecturePlan
        {
            BackendFiles =
            [
                new ArchitectureDeliverable("Acme.Repository/Repositories/TimesheetRepository.cs")
            ]
        };

        var findings = ExemplarTestCompanionSupport.ValidatePlannedProductionTests(repo.Path, plan).ToList();

        Assert.Contains(findings, finding => finding.Message.Contains("TimesheetRepositoryTests.cs", StringComparison.Ordinal));
    }
}
