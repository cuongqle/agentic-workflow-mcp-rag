using workflowX.Infrastructure;
using workflowX.Tests.Helpers;

namespace workflowX.PlugIns.DotNet.Tests.Infrastructure.Exemplar;

public class ProductionPathExemplarSupportTests
{
    [Fact]
    public void ValidatePlannedPaths_rejects_path_in_folder_not_used_by_same_kind_exemplar()
    {
        using TempRepo repo = new();
        repo.WriteFile(
            "Sample.Project/Indexes/CompanyIndex.cs",
            "public class CompanyIndex { }");

        var plan = new ArchitecturePlan
        {
            BackendFiles =
            [
                new ArchitectureDeliverable("Other.Project/Indexes/TimesheetIndex.cs", "planned")
            ]
        };

        var findings = ProductionPathExemplarSupport.ValidatePlannedPaths(repo.Path, plan).ToList();

        Assert.Single(findings);
        Assert.Contains("Sample.Project/Indexes", findings[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidatePlannedPaths_accepts_path_in_same_folder_group_as_exemplar()
    {
        using TempRepo repo = new();
        repo.WriteFile(
            "Sample.Project/Indexes/CompanyIndex.cs",
            "public class CompanyIndex { }");

        var plan = new ArchitecturePlan
        {
            BackendFiles =
            [
                new ArchitectureDeliverable("Sample.Project/Indexes/TimesheetIndex.cs", "planned")
            ]
        };

        Assert.Empty(ProductionPathExemplarSupport.ValidatePlannedPaths(repo.Path, plan));
    }
}
