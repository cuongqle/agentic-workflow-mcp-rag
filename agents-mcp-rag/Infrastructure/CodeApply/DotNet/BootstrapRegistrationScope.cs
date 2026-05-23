using agents_mcp_rag.Infrastructure.Compliance.DotNet;

using agents_mcp_rag.Infrastructure;

namespace agents_mcp_rag.Infrastructure.CodeApply.DotNet;

/// <summary>
/// Facade for DI registration scope discovery and RAG context formatting.
/// Delegates to <see cref="RegistrationScopeDiscoverer"/> and <see cref="RegistrationScopeConvention"/>.
/// </summary>
internal static class BootstrapRegistrationScope
{
    internal static string? BuildContext(string repoPath) =>
        BuildContext(RegistrationScopeDiscoverer.Discover(repoPath));

    internal static string? BuildContext(RegistrationScopeConvention convention) =>
        convention.FormatRagContext();

    internal static BootstrapScope? DiscoverFromContent(string content, string relativePath) =>
        RegistrationScopeDiscoverer.DiscoverFromContent(content, relativePath)?.ToBootstrapScope();

    internal static BootstrapScope? DiscoverPrimary(string repoPath)
    {
        RegistrationScopeConvention convention = RegistrationScopeDiscoverer.Discover(repoPath);
        return convention.IsDiscovered ? convention.ToBootstrapScope() : null;
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
            return RegistrationScopeConvention.None.BuildRegistrationLine(interfaceName, exemplarLine);
        }

        return scope.ToConvention().BuildRegistrationLine(interfaceName, exemplarLine);
    }

    internal static string FormatOutOfScopeError(BootstrapScope? scope) =>
        scope?.ToConvention().FormatOutOfScopeError()
        ?? RegistrationScopeConvention.None.FormatOutOfScopeError();

    internal sealed record BootstrapScope(
        string RelativePath,
        string CollectionVariable,
        string ReturnStatementPrefix,
        IReadOnlyList<string> SampleRegistrationLines)
    {
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
