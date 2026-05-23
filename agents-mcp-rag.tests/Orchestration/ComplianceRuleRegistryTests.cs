using agents_mcp_rag.Infrastructure;
using agents_mcp_rag.tests.Helpers;

namespace agents_mcp_rag.tests.Orchestration;

public class ComplianceRuleRegistryTests
{
    [Fact]
    public void For_none_stack_includes_only_shared_rules()
    {
        IReadOnlyList<IComplianceRule> rules = ComplianceRuleRegistry.For(RepoStack.None);

        Assert.Equal(2, rules.Count);
        Assert.DoesNotContain(rules, rule => rule.RuleId == "architecture.frontend-path-conventions");
    }

    [Fact]
    public void For_dotnet_stack_adds_dotnet_rules()
    {
        IReadOnlyList<IComplianceRule> rules = ComplianceRuleRegistry.For(new RepoStack(true, false));

        Assert.True(rules.Count > 2);
        Assert.Contains(rules, rule => rule.RuleId == "architecture.layer-contracts");
    }

    [Fact]
    public void For_full_stack_includes_frontend_and_dotnet_rules()
    {
        IReadOnlyList<IComplianceRule> rules = ComplianceRuleRegistry.For(new RepoStack(true, true));

        Assert.Contains(rules, rule => rule.RuleId == "architecture.frontend-path-conventions");
        Assert.Contains(rules, rule => rule.RuleId == "architecture.layer-contracts");
    }
}
