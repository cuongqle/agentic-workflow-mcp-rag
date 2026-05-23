using agents_mcp_rag.Infrastructure;
using agents_mcp_rag.tests.Helpers;

namespace agents_mcp_rag.tests.Infrastructure;

public class RepoContractDiscoveryTests
{
    [Fact]
    public void Discover_missing_repo_returns_empty_contract()
    {
        RepoContract contract = RepoContractDiscoverer.Discover("/path/does/not/exist");

        Assert.Equal("/path/does/not/exist", contract.RepoPath);
        Assert.False(contract.Stack.DotNet);
        Assert.False(contract.Stack.Frontend);
        Assert.Empty(contract.CompositionRootPaths);
    }

    [Fact]
    public void Discover_empty_directory_has_no_stacks()
    {
        using var repo = new TempRepo();
        RepoContract contract = RepoContractDiscoverer.Discover(repo.Path);

        Assert.False(contract.Stack.DotNet);
        Assert.False(contract.Stack.Frontend);
        Assert.Empty(contract.PathRules);
    }

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

        RepoContractDiscovery discovery = RepoContractComposer.Scan(repo.Path);

        Assert.NotEmpty(discovery.DotNet.CompositionRootPaths);
        Assert.Contains(
            discovery.DotNet.CompositionRootPaths,
            path => path.Contains("Bootstrapper.cs", StringComparison.OrdinalIgnoreCase));
        Assert.False(discovery.Frontend.IsDiscovered);
    }

    [Fact]
    public void Discover_finds_frontend_module_layout()
    {
        using var repo = new TempRepo();
        repo.WriteFile("web/modules/employee/controllers/list.js", "angular.module('x', []);");
        repo.WriteFile("web/modules/employee/views/list.html", "<div></div>");

        RepoContractDiscovery discovery = RepoContractComposer.Scan(repo.Path);

        Assert.True(discovery.Frontend.IsDiscovered);
        Assert.NotNull(discovery.Frontend.ModuleTemplate);
        Assert.Contains("modules", discovery.Frontend.ModuleTemplate!.ModulesRoot, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Compose_merges_independent_stack_signals()
    {
        using var repo = new TempRepo();
        repo.WriteFile("web/modules/employee/controllers/a.js", "const x = 1;");
        repo.WriteFile("web/modules/employee/views/a.html", "<p></p>");
        repo.WriteFile("src/App/Startup.cs", "// startup");

        RepoContract contract = RepoContractDiscoverer.Discover(repo.Path);

        Assert.True(contract.Stack.Frontend);
        Assert.True(contract.Stack.DotNet);
    }
}
