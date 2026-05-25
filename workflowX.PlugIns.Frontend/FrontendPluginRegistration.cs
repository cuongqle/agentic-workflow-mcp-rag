namespace workflowX.Infrastructure;

/// <summary>
/// Registers Frontend stack plug-in services with core registries.
/// </summary>
public static class FrontendPluginRegistration
{
    public static void Register()
    {
        StackModuleRegistry.Register(new FrontendStackModule());
    }
}
