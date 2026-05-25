using workflowX.Infrastructure;
using workflowX.Tests.Helpers;

namespace workflowX.PlugIns.DotNet.Tests.Orchestration;

public class DotNetComplianceRuleRegistryTests
{
    [Fact]
    public void For_dotnet_stack_adds_dotnet_rules()
    {
        IReadOnlyList<IComplianceRule> rules = ComplianceRuleRegistry.For(new RepoStack(true, false));

        Assert.True(rules.Count > 2);
        Assert.Contains(rules, rule => rule.RuleId == "architecture.layer-contracts");
        Assert.DoesNotContain(rules, rule => rule.RuleId == "architecture.frontend-path-conventions");
    }
}
