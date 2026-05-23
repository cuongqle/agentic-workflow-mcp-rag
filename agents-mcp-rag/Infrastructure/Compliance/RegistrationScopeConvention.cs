using agents_mcp_rag.Infrastructure.Compliance.DotNet;

namespace agents_mcp_rag.Infrastructure;

internal enum RegistrationFramework
{
    None,
    DotNetDependencyInjection
}

internal sealed record RegistrationScopeConvention(
    RegistrationFramework Framework,
    string HubRelativePath,
    string ReceiverExpression,
    string? BlockStartExample,
    string? BlockEndMarker,
    IReadOnlyList<string> SampleRegistrationLines)
{
    public static RegistrationScopeConvention None { get; } = new(
        RegistrationFramework.None,
        string.Empty,
        string.Empty,
        null,
        null,
        Array.Empty<string>());

    public bool IsDiscovered => Framework != RegistrationFramework.None;

    public string? FormatRagContext() =>
        Framework switch
        {
            RegistrationFramework.DotNetDependencyInjection => DotNetDiRegistrationDiscoverer.FormatRagContext(this),
            _ => null
        };

    public string BuildRegistrationLine(string interfaceName, string? exemplarLine = null) =>
        Framework switch
        {
            RegistrationFramework.DotNetDependencyInjection =>
                DotNetDiRegistrationDiscoverer.BuildRegistrationLine(this, interfaceName, exemplarLine),
            _ => string.Empty
        };

    public string FormatOutOfScopeError() =>
        Framework switch
        {
            RegistrationFramework.DotNetDependencyInjection => DotNetDiRegistrationDiscoverer.FormatOutOfScopeError(this),
            _ => "DI registration line is outside the discovered registration block."
        };

    internal BootstrapRegistrationScope.BootstrapScope ToBootstrapScope() =>
        new(
            HubRelativePath,
            ReceiverExpression,
            BlockEndMarker ?? string.Empty,
            SampleRegistrationLines);
}
