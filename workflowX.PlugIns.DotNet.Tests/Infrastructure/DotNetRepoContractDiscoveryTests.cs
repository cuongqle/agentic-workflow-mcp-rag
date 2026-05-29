using workflowX.Infrastructure;
using workflowX.Tests.Helpers;

namespace workflowX.PlugIns.DotNet.Tests.Infrastructure;

public class DotNetRepoContractDiscoveryTests
{
    [Fact]
    public void Discover_detects_dotnet_projects_without_composition_root_scan()
    {
        using var repo = new TempRepo();
        repo.WriteFile("src/Web/App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
        repo.WriteFile(
            "src/Web/App_Start/Bootstrapper.cs",
            """
            var services = new ServiceCollection();
            return services.BuildServiceProvider();
            """);

        RepoContract contract = RepoContractDiscoverer.Discover(repo.Path);

        Assert.True(contract.HasDotNetProjects);
        Assert.True(contract.Stack.DotNet);
        Assert.Empty(contract.CompositionRootPaths);
        Assert.False(contract.Stack.Frontend);
    }

    [Fact]
    public void Discover_entity_convention_from_on_disk_exemplars()
    {
        using var repo = new TempRepo();
        repo.WriteFile("src/App/App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
        repo.WriteFile(
            "src/App/Entities/Company.cs",
            """
            using App.Domain;

            public class Company : IEntity
            {
            }
            """);
        repo.WriteFile(
            "src/App/Entities/Project.cs",
            """
            using App.Domain;

            public class Project : IEntity
            {
            }
            """);

        RepoContract contract = RepoContractDiscoverer.Discover(repo.Path);

        Assert.NotNull(contract.Entity);
        Assert.Equal("src/App/Entities", contract.Entity!.CanonicalDirectory);
        Assert.Equal("IEntity", contract.Entity.RequiredInterface);
        Assert.Contains("using App.Domain", contract.Entity.RequiredUsingLine, StringComparison.Ordinal);
    }
}
