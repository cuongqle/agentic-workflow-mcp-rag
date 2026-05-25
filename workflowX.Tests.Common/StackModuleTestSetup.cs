using System.Runtime.CompilerServices;

namespace workflowX.Tests.Helpers;

internal static class StackModuleTestSetup
{
    [ModuleInitializer]
    internal static void InitializeModules() => StackModuleRegistration.RegisterDefaults();
}
