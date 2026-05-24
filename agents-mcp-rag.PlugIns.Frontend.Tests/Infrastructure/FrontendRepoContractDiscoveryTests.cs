using agents_mcp_rag.Infrastructure;
using agents_mcp_rag.Tests.Helpers;

namespace agents_mcp_rag.PlugIns.Frontend.Tests.Infrastructure;

public class FrontendRepoContractDiscoveryTests
{
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
}
