using workflowX.Infrastructure;
using workflowX.Tests.Helpers;

namespace workflowX.PlugIns.DotNet.Tests.Orchestration;

public class DotNetStackModuleRegistryTests
{
    [Fact]
    public void ComplianceRules_includes_dotnet_rules_only()
    {
        ResetModules();
        try
        {
            IEnumerable<IComplianceRule> dotnetRules = StackModuleRegistry.ComplianceRules(new RepoStack(true, false));

            Assert.DoesNotContain(dotnetRules, rule => rule.RuleId == "testing.missing-tests");
            Assert.DoesNotContain(dotnetRules, rule => rule.RuleId == "architecture.frontend-path-conventions");
        }
        finally
        {
            RestoreDefaultModules();
        }
    }

    private static void ResetModules()
    {
        StackModuleRegistration.ResetForTests();
        StackModuleRegistration.RegisterDefaults();
    }

    private static void RestoreDefaultModules()
    {
        StackModuleRegistration.ResetForTests();
        StackModuleRegistration.RegisterDefaults();
    }
}
