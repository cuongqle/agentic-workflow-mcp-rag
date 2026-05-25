using workflowX.Infrastructure;
using workflowX.Tests.Helpers;

namespace workflowX.Tests.Orchestration;

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
