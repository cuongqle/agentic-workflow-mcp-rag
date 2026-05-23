namespace agents_mcp_rag.Infrastructure;

public enum RegistrationFramework
{
    None,
    DotNetDependencyInjection
}

/// <summary>
/// Stack-agnostic dependency-registration scope discovered from the target repository.
/// Framework-specific behavior lives in stack plug-in assemblies.
/// </summary>
public sealed record RegistrationScopeConvention(
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
}
