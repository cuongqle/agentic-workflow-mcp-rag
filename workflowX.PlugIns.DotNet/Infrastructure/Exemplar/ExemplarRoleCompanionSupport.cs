using System.Text.RegularExpressions;

namespace workflowX.Infrastructure.Exemplar.DotNet;

/// <summary>
/// Discovers cross-role deliverable companions from same-kind exemplar sources (types referenced in exemplar bodies).
/// </summary>
internal static class ExemplarRoleCompanionSupport
{
    private static readonly Regex TypeReferenceRegex = new(
        @"\b([A-Za-z_][A-Za-z0-9_]*)\b",
        RegexOptions.Compiled);

    internal static IReadOnlyList<string> DiscoverMissingCompanionPaths(
        string repoPath,
        IReadOnlyList<string> plannedPaths)
    {
        if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath) || plannedPaths.Count == 0)
        {
            return Array.Empty<string>();
        }

        IReadOnlyList<string> productionPaths = ProductionPathExemplarSupport.DiscoverProductionRelativePaths(repoPath);
        if (productionPaths.Count == 0)
        {
            return Array.Empty<string>();
        }

        var plannedSet = new HashSet<string>(
            plannedPaths.Select(ProductionPathExemplarSupport.NormalizePath),
            StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();

        foreach (string plannedPath in plannedPaths)
        {
            foreach (string companion in DiscoverPlannedCompanionPaths(repoPath, plannedPath, productionPaths))
            {
                if (plannedSet.Add(companion))
                {
                    missing.Add(companion);
                }
            }
        }

        return missing;
    }

    internal static IReadOnlyList<string> DiscoverCompanionExemplarPaths(
        string repoPath,
        string plannedPath,
        IReadOnlyList<string> productionPaths)
    {
        if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
        {
            return Array.Empty<string>();
        }

        string normalizedPlanned = ProductionPathExemplarSupport.NormalizePath(plannedPath);
        if (!TryGetSubjectAndRoleFromPath(normalizedPlanned, out _, out string plannedRole))
        {
            return Array.Empty<string>();
        }

        var exemplars = new List<string>();
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

            string content = File.ReadAllText(absolute);
            exemplars.AddRange(
                DiscoverReferencedCompanionExemplarPaths(content, exemplarSubject, plannedRole, productionPaths));
        }

        return exemplars
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> DiscoverPlannedCompanionPaths(
        string repoPath,
        string plannedPath,
        IReadOnlyList<string> productionPaths)
    {
        string normalizedPlanned = ProductionPathExemplarSupport.NormalizePath(plannedPath);
        if (!TryGetSubjectAndRoleFromPath(normalizedPlanned, out string plannedSubject, out string plannedRole))
        {
            yield break;
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

            string content = File.ReadAllText(absolute);
            foreach (string companionExemplar in DiscoverReferencedCompanionExemplarPaths(
                         content,
                         exemplarSubject,
                         plannedRole,
                         productionPaths))
            {
                if (!TryGetSubjectAndRoleFromPath(companionExemplar, out _, out string companionRole))
                {
                    continue;
                }

                yield return BuildPlannedCompanionPath(companionExemplar, plannedSubject, companionRole);
            }
        }
    }

    private static IEnumerable<string> DiscoverReferencedCompanionExemplarPaths(
        string exemplarContent,
        string exemplarSubject,
        string plannedRole,
        IReadOnlyList<string> productionPaths)
    {
        var emittedRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string productionPath in productionPaths)
        {
            string stem = Path.GetFileNameWithoutExtension(Path.GetFileName(productionPath));
            if (!DeliverableFileNameSupport.TryGetSubjectAndRole(stem, out string subject, out string roleSuffix))
            {
                continue;
            }

            if (roleSuffix.Equals(plannedRole, StringComparison.OrdinalIgnoreCase)
                || !subject.Equals(exemplarSubject, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string typeName = subject + roleSuffix;
            if (!ContainsTypeReference(exemplarContent, typeName))
            {
                continue;
            }

            if (!emittedRoles.Add(roleSuffix))
            {
                continue;
            }

            yield return productionPath;
        }
    }

    private static string BuildPlannedCompanionPath(
        string companionExemplarPath,
        string plannedSubject,
        string companionRole)
    {
        string normalized = ProductionPathExemplarSupport.NormalizePath(companionExemplarPath);
        string? directory = Path.GetDirectoryName(normalized)?.Replace('\\', '/');
        string extension = Path.GetExtension(normalized);
        string fileName = plannedSubject + companionRole + extension;
        return string.IsNullOrWhiteSpace(directory) ? fileName : $"{directory}/{fileName}";
    }

    private static bool ContainsTypeReference(string content, string typeName) =>
        TypeReferenceRegex.IsMatch(content)
        && content.Contains(typeName, StringComparison.Ordinal);

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
