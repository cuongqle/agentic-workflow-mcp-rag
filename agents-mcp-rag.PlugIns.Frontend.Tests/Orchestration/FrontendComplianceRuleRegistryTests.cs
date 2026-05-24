using agents_mcp_rag.Infrastructure;
using agents_mcp_rag.Tests.Helpers;

namespace agents_mcp_rag.PlugIns.Frontend.Tests.Orchestration;

public class FrontendComplianceRuleRegistryTests
{
    [Fact]
    public void For_frontend_stack_adds_frontend_rules()
    {
        IReadOnlyList<IComplianceRule> rules = ComplianceRuleRegistry.For(new RepoStack(false, true));

        Assert.True(rules.Count > 2);
        Assert.Contains(rules, rule => rule.RuleId == "architecture.frontend-path-conventions");
        Assert.DoesNotContain(rules, rule => rule.RuleId == "architecture.layer-contracts");
    }
}
