/// <summary>
/// Registers built-in stack modules. Host calls at startup; tests call after <see cref="StackModuleRegistry.Reset"/>.
/// </summary>
static class StackModuleRegistration
{
    public static void RegisterDefaults()
    {
        if (StackModuleRegistry.IsInitialized)
        {
            return;
        }

        DotNetPluginRegistration.Register();
        StackModuleRegistry.Register(new FrontendStackModule());
    }
}
