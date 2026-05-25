using workflowX.Infrastructure;
using workflowX.Tests.Helpers;

namespace workflowX.PlugIns.Frontend.Tests.Orchestration;

public class FrontendStackModuleRegistryTests
{
    [Fact]
    public void ComplianceRules_includes_frontend_rules_only()
    {
        ResetModules();
        try
        {
            IEnumerable<IComplianceRule> frontendRules = StackModuleRegistry.ComplianceRules(new RepoStack(false, true));

            Assert.Contains(frontendRules, rule => rule.RuleId == "architecture.frontend-path-conventions");
            Assert.DoesNotContain(frontendRules, rule => rule.RuleId == "architecture.layer-contracts");
        }
        finally
        {
            RestoreDefaultModules();
        }
    }

    [Fact]
    public void TestReleasePolicies_not_registered_for_frontend()
    {
        ResetModules();
        try
        {
            Assert.Empty(StackModuleRegistry.TestReleasePolicies(new RepoStack(false, true)));
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
