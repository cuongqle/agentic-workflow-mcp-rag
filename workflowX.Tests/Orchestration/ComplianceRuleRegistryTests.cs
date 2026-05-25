using workflowX.Infrastructure;

namespace workflowX.Tests.Orchestration;

public class ComplianceRuleRegistryTests
{
    [Fact]
    public void For_none_stack_includes_only_shared_rules()
    {
        IReadOnlyList<IComplianceRule> rules = ComplianceRuleRegistry.For(RepoStack.None);

        Assert.Equal(2, rules.Count);
        Assert.DoesNotContain(rules, rule => rule.RuleId == "architecture.frontend-path-conventions");
        Assert.DoesNotContain(rules, rule => rule.RuleId == "architecture.layer-contracts");
    }

    [Fact]
    public void For_full_stack_includes_frontend_and_dotnet_rules()
    {
        IReadOnlyList<IComplianceRule> rules = ComplianceRuleRegistry.For(new RepoStack(true, true));

        Assert.Contains(rules, rule => rule.RuleId == "architecture.frontend-path-conventions");
        Assert.Contains(rules, rule => rule.RuleId == "architecture.layer-contracts");
    }
}
