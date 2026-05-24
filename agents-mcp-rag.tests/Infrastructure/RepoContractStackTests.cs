using agents_mcp_rag.Infrastructure;

namespace agents_mcp_rag.Tests.Infrastructure;

public class RepoContractStackTests
{
    [Fact]
    public void Stack_none_when_no_signals()
    {
        var contract = new RepoContract
        {
            RepoPath = "/repo",
            RegistrationScope = RegistrationScopeConvention.None
        };

        Assert.False(contract.Stack.DotNet);
        Assert.False(contract.Stack.Frontend);
        Assert.Equal(RepoStack.None, contract.Stack);
    }
}
