using agents_mcp_rag.Infrastructure;

namespace agents_mcp_rag.Tests.Infrastructure;

public class RepoStackTests
{
    [Fact]
    public void From_contract_maps_backend_and_frontend_flags()
    {
        var contract = new RepoContract
        {
            RepoPath = "/repo",
            LayerConventions = new LayerConventionProfiles(
            [
                new LayerConventionProfile(
                    "repository",
                    "Repository.cs",
                    1,
                    "src/Repositories",
                    true,
                    true,
                    false,
                    [],
                    [],
                    LayerInterfacePairingConvention.None)
            ]),
            Frontend = new FrontendModuleTemplate(
                "web/modules",
                "web",
                "home",
                FrontendLayoutMode.HostModulePages,
                [],
                [],
                [],
                [])
        };

        RepoStack stack = RepoStack.From(contract);

        Assert.True(stack.DotNet);
        Assert.True(stack.Frontend);
    }

    [Fact]
    public void WhenDotNet_runs_only_for_dotnet_stack()
    {
        var stack = new RepoStack(DotNet: true, Frontend: false);
        int calls = 0;
        stack.WhenDotNet(() => calls++);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void WhenDotNet_otherwise_routes_to_fallback()
    {
        var stack = new RepoStack(DotNet: false, Frontend: true);
        string result = stack.DotNetOr("dotnet", "other");
        Assert.Equal("other", result);
    }

    [Fact]
    public void WhenFrontend_items_empty_when_stack_absent()
    {
        var stack = new RepoStack(DotNet: false, Frontend: false);
        Assert.Empty(stack.WhenDotNet(["a", "b"]).ToList());
        Assert.Empty(stack.WhenFrontend(["x"]).ToList());
    }

    [Fact]
    public void WhenFrontend_items_returned_when_stack_present()
    {
        var stack = new RepoStack(DotNet: false, Frontend: true);
        Assert.Equal(["x"], stack.WhenFrontend(["x"]).ToList());
    }
}
