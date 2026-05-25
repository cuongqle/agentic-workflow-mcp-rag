/// <summary>
/// Registers built-in stack modules. Host calls at startup; tests call after <see cref="StackModuleRegistry.Reset"/>.
/// </summary>
static class StackModuleRegistration
{
    private static bool _defaultsRegistered;
    private static readonly object Gate = new();

    public static void RegisterDefaults()
    {
        lock (Gate)
        {
            if (_defaultsRegistered)
            {
                return;
            }

            DotNetPluginRegistration.Register();
            FrontendPluginRegistration.Register();
            _defaultsRegistered = true;
        }
    }

    internal static void ResetForTests()
    {
        lock (Gate)
        {
            StackModuleRegistry.Reset();
            _defaultsRegistered = false;
        }
    }
}
