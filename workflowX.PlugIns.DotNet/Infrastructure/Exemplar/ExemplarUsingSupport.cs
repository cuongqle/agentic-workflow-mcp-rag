using System.Text;
using System.Text.RegularExpressions;

namespace workflowX.Infrastructure.Exemplar.DotNet;

/// <summary>
/// Discovers required using directives and namespaces from on-disk exemplar sources.
/// </summary>
internal static class ExemplarUsingSupport
{
    private static readonly Regex UsingLineRegex = new(
        @"^\s*using\s+(?:static\s+)?([^;]+);",
        RegexOptions.Multiline);

    private static readonly Regex NamespaceDeclarationRegex = new(
        @"^\s*namespace\s+([A-Za-z_][A-Za-z0-9_.]*)\s*[;{]",
        RegexOptions.Multiline);

    internal static string BuildImplementationUsingHints(
        string repoPath,
        IReadOnlyList<string> plannedPaths,
        IReadOnlyList<string> productionPaths)
    {
        if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath) || plannedPaths.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        foreach (string plannedPath in plannedPaths)
        {
            AppendHintsForPlannedPath(repoPath, plannedPath, productionPaths, sb);
        }

        return sb.ToString().TrimEnd();
    }

    private static void AppendHintsForPlannedPath(
        string repoPath,
        string plannedPath,
        IReadOnlyList<string> productionPaths,
        StringBuilder sb)
    {
        string normalizedPlanned = ProductionPathExemplarSupport.NormalizePath(plannedPath);
        if (!TryGetSubjectAndRoleFromPath(normalizedPlanned, out string plannedSubject, out string plannedRole))
        {
            return;
        }

        foreach (string exemplarPath in SelectSameKindExemplarPaths(productionPaths, normalizedPlanned, plannedRole))
        {
            string absolute = Path.Combine(repoPath, exemplarPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(absolute))
            {
                continue;
            }

            if (!TryGetSubjectAndRoleFromPath(exemplarPath, out string exemplarSubject, out _))
            {
                continue;
            }

            string exemplarContent = File.ReadAllText(absolute);
            IReadOnlyList<string> exemplarUsings = ExtractUsingLines(exemplarContent);
            if (exemplarUsings.Count > 0)
            {
                sb.AppendLine($"- {normalizedPlanned}: copy the exemplar using block from {exemplarPath}:");
                foreach (string usingLine in exemplarUsings)
                {
                    sb.AppendLine($"    {usingLine}");
                }
            }

            if (TryGetDeclaredNamespace(exemplarContent, out string exemplarNamespace))
            {
                sb.AppendLine(
                    $"- {normalizedPlanned}: declare namespace `{exemplarNamespace}` (same as exemplar {exemplarPath}; change only type names).");
            }

            foreach (string companionExemplar in ExemplarRoleCompanionSupport.DiscoverCompanionExemplarPaths(
                         repoPath,
                         normalizedPlanned,
                         productionPaths))
            {
                AppendCrossRoleUsingHint(
                    sb,
                    normalizedPlanned,
                    plannedSubject,
                    exemplarSubject,
                    exemplarPath,
                    exemplarContent,
                    exemplarUsings,
                    companionExemplar,
                    repoPath);
            }
        }
    }

    private static void AppendCrossRoleUsingHint(
        StringBuilder sb,
        string plannedPath,
        string plannedSubject,
        string exemplarSubject,
        string hostExemplarPath,
        string hostExemplarContent,
        IReadOnlyList<string> hostExemplarUsings,
        string companionExemplarPath,
        string repoPath)
    {
        if (!TryGetSubjectAndRoleFromPath(companionExemplarPath, out _, out string companionRole))
        {
            return;
        }

        string absolute = Path.Combine(repoPath, companionExemplarPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(absolute))
        {
            return;
        }

        string companionContent = File.ReadAllText(absolute);
        if (!TryGetDeclaredNamespace(companionContent, out string companionNamespace))
        {
            return;
        }

        string exemplarTypeName = exemplarSubject + companionRole;
        string plannedTypeName = plannedSubject + companionRole;
        if (!hostExemplarContent.Contains(exemplarTypeName, StringComparison.Ordinal))
        {
            return;
        }

        string? requiredUsing = FindUsingForNamespace(hostExemplarUsings, companionNamespace);
        if (string.IsNullOrWhiteSpace(requiredUsing))
        {
            return;
        }

        sb.AppendLine(
            $"- {plannedPath}: when using `{plannedTypeName}`, include `{requiredUsing}` "
            + $"(exemplar {hostExemplarPath} uses it for `{exemplarTypeName}` in namespace `{companionNamespace}`).");
    }

    internal static IReadOnlyList<string> ExtractUsingLines(string content)
    {
        var usings = new List<string>();
        foreach (Match match in UsingLineRegex.Matches(content))
        {
            string imported = match.Groups[1].Value.Trim();
            if (string.IsNullOrWhiteSpace(imported))
            {
                continue;
            }

            usings.Add($"using {imported};");
        }

        return usings;
    }

    internal static bool TryGetDeclaredNamespace(string content, out string namespaceName)
    {
        namespaceName = string.Empty;
        Match match = NamespaceDeclarationRegex.Match(content);
        if (!match.Success)
        {
            return false;
        }

        namespaceName = match.Groups[1].Value;
        return !string.IsNullOrWhiteSpace(namespaceName);
    }

    private static string? FindUsingForNamespace(IReadOnlyList<string> usingLines, string typeNamespace)
    {
        string? best = null;
        int bestLength = -1;
        foreach (string usingLine in usingLines)
        {
            if (!TryParseImportedNamespace(usingLine, out string imported))
            {
                continue;
            }

            if (!typeNamespace.Equals(imported, StringComparison.Ordinal)
                && !typeNamespace.StartsWith(imported + ".", StringComparison.Ordinal))
            {
                continue;
            }

            if (imported.Length > bestLength)
            {
                bestLength = imported.Length;
                best = usingLine;
            }
        }

        return best;
    }

    private static bool TryParseImportedNamespace(string usingLine, out string importedNamespace)
    {
        importedNamespace = string.Empty;
        Match match = UsingLineRegex.Match(usingLine);
        if (!match.Success)
        {
            return false;
        }

        importedNamespace = match.Groups[1].Value.Trim();
        return !string.IsNullOrWhiteSpace(importedNamespace);
    }

    private static IEnumerable<string> SelectSameKindExemplarPaths(
        IReadOnlyList<string> productionPaths,
        string plannedPath,
        string plannedRole)
    {
        string? plannedParent = Path.GetDirectoryName(plannedPath)?.Replace('\\', '/');
        return productionPaths
            .Where(path => !path.Equals(plannedPath, StringComparison.OrdinalIgnoreCase))
            .Where(path => TryGetSubjectAndRoleFromPath(path, out _, out string role)
                && role.Equals(plannedRole, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(path => SharesParentDirectory(path, plannedParent))
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Take(2);
    }

    private static bool TryGetSubjectAndRoleFromPath(string relativePath, out string subject, out string roleSuffix)
    {
        string stem = Path.GetFileNameWithoutExtension(Path.GetFileName(relativePath));
        return DeliverableFileNameSupport.TryGetSubjectAndRole(stem, out subject, out roleSuffix);
    }

    private static bool SharesParentDirectory(string path, string? parentDirectory)
    {
        if (string.IsNullOrWhiteSpace(parentDirectory))
        {
            return false;
        }

        string? pathParent = Path.GetDirectoryName(path.Replace('\\', '/'))?.Replace('\\', '/');
        return parentDirectory.Equals(pathParent, StringComparison.OrdinalIgnoreCase);
    }
}
