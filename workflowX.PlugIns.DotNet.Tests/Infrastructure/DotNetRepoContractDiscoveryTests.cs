using workflowX.Infrastructure;
using workflowX.Tests.Helpers;

namespace workflowX.PlugIns.DotNet.Tests.Infrastructure;

public class DotNetRepoContractDiscoveryTests
{
    [Fact]
    public void Discover_finds_dotnet_composition_root_bootstrapper()
    {
        using var repo = new TempRepo();
        repo.WriteFile(
            "src/Web/App_Start/Bootstrapper.cs",
            """
            var services = new ServiceCollection();
            return services.BuildServiceProvider();
            """);

        RepoContract contract = RepoContractDiscoverer.Discover(repo.Path);

        Assert.NotEmpty(contract.CompositionRootPaths);
        Assert.Contains(
            contract.CompositionRootPaths,
            path => path.Contains("Bootstrapper.cs", StringComparison.OrdinalIgnoreCase));
        Assert.False(contract.Stack.Frontend);
    }
}
