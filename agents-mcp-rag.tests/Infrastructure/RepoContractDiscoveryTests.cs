using agents_mcp_rag.Infrastructure;
using agents_mcp_rag.Tests.Helpers;

namespace agents_mcp_rag.Tests.Infrastructure;

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
