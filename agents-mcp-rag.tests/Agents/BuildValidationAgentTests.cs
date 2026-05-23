using agents_mcp_rag.Infrastructure;

namespace agents_mcp_rag.tests.Agents;

public class BuildValidationAgentTests
{
    [Fact]
    public async Task ExecuteAsync_skips_when_no_supported_stack_detected()
    {
        var agent = new BuildValidationAgent();
        var state = new WorkflowState
        {
            RepoPath = "/nonexistent",
            Contract = new RepoContract
            {
                RepoPath = "/nonexistent",
                RegistrationScope = RegistrationScopeConvention.None
            }
        };

        AgentResult result = await agent.ExecuteAsync(state);

        Assert.Contains("skipped", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Single(result.Findings);
        Assert.Equal(FindingSeverity.Medium, result.Findings[0].Severity);
    }
}
