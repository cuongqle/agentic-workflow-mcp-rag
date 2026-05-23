using System.Runtime.CompilerServices;

namespace agents_mcp_rag.Tests.Helpers;

internal static class StackModuleTestSetup
{
    [ModuleInitializer]
    internal static void InitializeModules() => StackModuleRegistration.RegisterDefaults();
}
