using workflowX.Infrastructure.Compliance.DotNet;

namespace workflowX.Infrastructure;

/// <summary>
/// Discovers dependency-registration scope conventions for the target repository.
/// Stack-specific discoverers live under <see cref="Compliance.DotNet"/>.
/// </summary>
internal static class RegistrationScopeDiscoverer
{
    internal static RegistrationScopeConvention Discover(string repoPath) =>
        DotNetDiRegistrationDiscoverer.DiscoverPrimary(repoPath);

    internal static RegistrationScopeConvention? DiscoverFromContent(string content, string relativePath) =>
        DotNetDiRegistrationDiscoverer.TryParseFromContent(content, relativePath);
}
