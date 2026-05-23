using agents_mcp_rag.Infrastructure;
using agents_mcp_rag.Tests.Helpers;

namespace agents_mcp_rag.Tests.Orchestration;

public class StackModuleRegistryTests
{
    [Fact]
    public void RegisterDefaults_registers_dotnet_and_frontend_modules()
    {
        ResetModules();
        try
        {
            Assert.True(StackModuleRegistry.IsInitialized);
            Assert.Single(StackModuleRegistry.Active(new RepoStack(true, false)));
            Assert.Single(StackModuleRegistry.Active(new RepoStack(false, true)));
            Assert.Equal(2, StackModuleRegistry.Active(new RepoStack(true, true)).Count());
        }
        finally
        {
            RestoreDefaultModules();
        }
    }

    [Fact]
    public void ComplianceRules_comes_from_active_modules_only()
    {
        ResetModules();
        try
        {
            IEnumerable<IComplianceRule> dotnetRules = StackModuleRegistry.ComplianceRules(new RepoStack(true, false));
            IEnumerable<IComplianceRule> frontendRules = StackModuleRegistry.ComplianceRules(new RepoStack(false, true));

            Assert.Contains(dotnetRules, rule => rule.RuleId == "architecture.layer-contracts");
            Assert.DoesNotContain(dotnetRules, rule => rule.RuleId == "architecture.frontend-path-conventions");
            Assert.Contains(frontendRules, rule => rule.RuleId == "architecture.frontend-path-conventions");
            Assert.DoesNotContain(frontendRules, rule => rule.RuleId == "architecture.layer-contracts");
        }
        finally
        {
            RestoreDefaultModules();
        }
    }

    [Fact]
    public void TestReleasePolicies_only_registered_for_dotnet()
    {
        ResetModules();
        try
        {
            Assert.Empty(StackModuleRegistry.TestReleasePolicies(new RepoStack(false, true)));
            Assert.Single(StackModuleRegistry.TestReleasePolicies(new RepoStack(true, false)));
        }
        finally
        {
            RestoreDefaultModules();
        }
    }

    private static void ResetModules()
    {
        StackModuleRegistry.Reset();
        StackModuleRegistration.RegisterDefaults();
    }

    private static void RestoreDefaultModules()
    {
        StackModuleRegistry.Reset();
        StackModuleRegistration.RegisterDefaults();
    }
}
