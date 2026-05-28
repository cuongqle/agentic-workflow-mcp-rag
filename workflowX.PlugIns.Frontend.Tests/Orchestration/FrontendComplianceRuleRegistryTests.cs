using workflowX.Infrastructure;
using workflowX.Tests.Helpers;

namespace workflowX.PlugIns.Frontend.Tests.Orchestration;

public class FrontendComplianceRuleRegistryTests
{
    [Fact]
    public void For_frontend_stack_adds_frontend_rules()
    {
        IReadOnlyList<IComplianceRule> rules = ComplianceRuleRegistry.For(new RepoStack(false, true));

        Assert.Equal(2, rules.Count);
        Assert.Contains(rules, rule => rule.RuleId == "architecture.frontend-path-conventions");
        Assert.DoesNotContain(rules, rule => rule.RuleId == "architecture.layer-contracts");
    }
}
