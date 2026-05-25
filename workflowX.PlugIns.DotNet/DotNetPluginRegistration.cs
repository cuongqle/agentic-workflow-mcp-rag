namespace workflowX.Infrastructure;

/// <summary>
/// Registers DotNet stack plug-in services with core registries.
/// </summary>
public static class DotNetPluginRegistration
{
    public static void Register()
    {
        StackModuleRegistry.Register(new DotNetStackModule());
    }
}
