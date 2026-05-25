namespace workflowX.Infrastructure;

public readonly record struct ApplyIssue(string RelativePath, string Reason);

public readonly record struct ApplyResult(
    IReadOnlyList<string> AppliedFiles,
    IReadOnlyList<ApplyIssue> RejectedFiles,
    IReadOnlyList<AppliedFileChange> AppliedChanges);

public readonly record struct AppliedFileChange(
    string RelativePath,
    bool ExistedBeforeApply,
    string? PreviousContent);

public sealed class InterfaceCatalog
{
    private readonly Dictionary<string, HashSet<string>> _methods;

    public InterfaceCatalog(Dictionary<string, HashSet<string>> methods)
    {
        _methods = methods;
    }

    public bool TryGetMethods(string interfaceName, out HashSet<string> methods)
    {
        return _methods.TryGetValue(interfaceName, out methods!);
    }
}

public sealed class TypeNamespaceCatalog
{
    private readonly Dictionary<string, HashSet<string>> _namespacesByType;

    public TypeNamespaceCatalog(Dictionary<string, HashSet<string>> namespacesByType)
    {
        _namespacesByType = namespacesByType;
    }

    public bool IsEmpty => _namespacesByType.Count == 0;

    public bool TryGetUniqueNamespace(string typeName, out string? ns)
    {
        ns = null;
        if (!_namespacesByType.TryGetValue(typeName, out var namespaces))
        {
            return false;
        }

        if (namespaces.Count != 1)
        {
            return false;
        }

        ns = namespaces.FirstOrDefault();
        return !string.IsNullOrWhiteSpace(ns);
    }
}
