using agents_mcp_rag.Infrastructure;
using agents_mcp_rag.Tests.Helpers;

namespace agents_mcp_rag.PlugIns.DotNet.Tests.Infrastructure;

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
