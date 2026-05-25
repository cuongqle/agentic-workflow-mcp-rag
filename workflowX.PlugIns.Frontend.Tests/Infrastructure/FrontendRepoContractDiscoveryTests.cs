using workflowX.Infrastructure;
using workflowX.Tests.Helpers;

namespace workflowX.PlugIns.Frontend.Tests.Infrastructure;

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
