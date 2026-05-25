using workflowX.Infrastructure.Compliance.DotNet;

using workflowX.Infrastructure;

namespace workflowX.Infrastructure.CodeApply.DotNet;

/// <summary>
/// Facade for DI registration scope discovery and RAG context formatting.
/// Delegates to <see cref="RegistrationScopeDiscoverer"/> and DotNet registration helpers.
/// </summary>
internal static class BootstrapRegistrationScope
{
    internal static string? BuildContext(string repoPath) =>
        BuildContext(RegistrationScopeDiscoverer.Discover(repoPath));

    internal static string? BuildContext(RegistrationScopeConvention convention) =>
        DotNetDiRegistrationDiscoverer.FormatRagContext(convention);

    internal static BootstrapScope? DiscoverFromContent(string content, string relativePath) =>
        RegistrationScopeDiscoverer.DiscoverFromContent(content, relativePath) is { IsDiscovered: true } convention
            ? BootstrapScope.FromConvention(convention)
            : null;

    internal static BootstrapScope? DiscoverPrimary(string repoPath)
    {
        RegistrationScopeConvention convention = RegistrationScopeDiscoverer.Discover(repoPath);
        return convention.IsDiscovered ? BootstrapScope.FromConvention(convention) : null;
    }

    internal static bool TryFindRegistrationBlock(
        IReadOnlyList<string> lines,
        out int collectionDeclIndex,
        out int insertBeforeIndex,
        out string collectionVariable) =>
        DotNetDiRegistrationDiscoverer.TryFindRegistrationBlock(
            lines,
            out collectionDeclIndex,
            out insertBeforeIndex,
            out collectionVariable);

    internal static bool IsRegistrationLine(string line, string collectionVariable) =>
        DotNetDiRegistrationDiscoverer.IsRegistrationLine(line, collectionVariable);

    internal static string BuildRegistrationLine(BootstrapScope? scope, string interfaceName, string? exemplarLine = null)
    {
        if (scope is null)
        {
            return DotNetDiRegistrationDiscoverer.BuildRegistrationLine(
                RegistrationScopeConvention.None,
                interfaceName,
                exemplarLine);
        }

        return DotNetDiRegistrationDiscoverer.BuildRegistrationLine(
            scope.ToConvention(),
            interfaceName,
            exemplarLine);
    }

    internal static string FormatOutOfScopeError(BootstrapScope? scope) =>
        scope is null
            ? DotNetDiRegistrationDiscoverer.FormatOutOfScopeError(RegistrationScopeConvention.None)
            : DotNetDiRegistrationDiscoverer.FormatOutOfScopeError(scope.ToConvention());

    internal sealed record BootstrapScope(
        string RelativePath,
        string CollectionVariable,
        string ReturnStatementPrefix,
        IReadOnlyList<string> SampleRegistrationLines)
    {
        internal static BootstrapScope FromConvention(RegistrationScopeConvention convention) =>
            new(
                convention.HubRelativePath,
                convention.ReceiverExpression,
                convention.BlockEndMarker ?? string.Empty,
                convention.SampleRegistrationLines);

        internal RegistrationScopeConvention ToConvention() =>
            new(
                RegistrationFramework.DotNetDependencyInjection,
                RelativePath,
                CollectionVariable,
                $"var {CollectionVariable} = new ServiceCollection()",
                ReturnStatementPrefix,
                SampleRegistrationLines);
    }
}
