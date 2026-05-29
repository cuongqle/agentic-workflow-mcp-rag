using System.Text.RegularExpressions;

namespace workflowX.Infrastructure;

/// <summary>
/// Discovers .NET repo-contract signals from on-disk sources (no hard-coded type or folder names).
/// </summary>
internal static class DotNetRepoContractDiscoverer
{
    private static readonly Regex ClassImplementsRegex = new(
        @"\bclass\s+[A-Za-z_][A-Za-z0-9_]*\s*:\s*([A-Za-z_][A-Za-z0-9_.]*)",
        RegexOptions.Multiline);

    private static readonly Regex UsingLineRegex = new(
        @"^\s*using\s+([^;]+);",
        RegexOptions.Multiline);

    internal static EntityConvention? DiscoverEntityConvention(string repoPath)
    {
        if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
        {
            return null;
        }

        IReadOnlyList<string> paths = ProductionPathExemplarSupport.DiscoverProductionRelativePaths(repoPath);
        var byDirectory = new Dictionary<string, List<(string Path, string InterfaceName)>>(StringComparer.OrdinalIgnoreCase);

        foreach (string relativePath in paths)
        {
            string absolute = Path.Combine(repoPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(absolute))
            {
                continue;
            }

            string content = File.ReadAllText(absolute);
            Match match = ClassImplementsRegex.Match(content);
            if (!match.Success)
            {
                continue;
            }

            string? parent = Path.GetDirectoryName(relativePath.Replace('\\', '/'))?.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(parent))
            {
                continue;
            }

            string interfaceName = Path.GetFileName(match.Groups[1].Value);
            if (!interfaceName.StartsWith('I') || interfaceName.Length <= 1)
            {
                continue;
            }

            if (!byDirectory.TryGetValue(parent, out List<(string Path, string InterfaceName)>? list))
            {
                list = new List<(string Path, string InterfaceName)>();
                byDirectory[parent] = list;
            }

            list.Add((relativePath, interfaceName));
        }

        if (byDirectory.Count == 0)
        {
            return null;
        }

        (string directory, List<(string Path, string InterfaceName)> entries) = byDirectory
            .OrderByDescending(kvp => kvp.Value.Count)
            .ThenBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .First();

        string exemplarPath = entries[0].Path;
        string requiredInterface = entries
            .GroupBy(entry => entry.InterfaceName, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .First()
            .Key;

        string? requiredUsing = TryExtractUsingForType(repoPath, exemplarPath, requiredInterface);

        return new EntityConvention(
            directory,
            requiredInterface,
            exemplarPath,
            requiredUsing);
    }

    private static string? TryExtractUsingForType(string repoPath, string exemplarRelativePath, string typeName)
    {
        string absolute = Path.Combine(repoPath, exemplarRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(absolute))
        {
            return null;
        }

        string content = File.ReadAllText(absolute);
        string? namespaceUsing = null;
        foreach (Match match in UsingLineRegex.Matches(content))
        {
            string imported = match.Groups[1].Value.Trim();
            if (imported.EndsWith("." + typeName, StringComparison.Ordinal)
                || imported.Equals(typeName, StringComparison.Ordinal))
            {
                return $"using {imported};";
            }

            if (imported.StartsWith("System", StringComparison.Ordinal))
            {
                continue;
            }

            if (imported.Contains('.'))
            {
                namespaceUsing ??= $"using {imported};";
            }
        }

        return namespaceUsing;
    }
}
