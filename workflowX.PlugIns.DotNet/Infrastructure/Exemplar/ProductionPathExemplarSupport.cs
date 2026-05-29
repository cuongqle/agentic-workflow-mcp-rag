namespace workflowX.Infrastructure.Exemplar.DotNet;

/// <summary>
/// Discovered on-disk folder groups and planned-path checks (no hard-coded project or artifact names).
/// </summary>
internal static class ProductionPathExemplarSupport
{
    internal static IEnumerable<AgentFinding> ValidatePlannedPaths(string repoPath, ArchitecturePlan? plan)
    {
        if (plan is null || string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
        {
            yield break;
        }

        IReadOnlyDictionary<string, HashSet<string>> foldersByRoleSuffix = DiscoverParentFoldersByRoleSuffix(repoPath);
        if (foldersByRoleSuffix.Count == 0)
        {
            yield break;
        }

        foreach (ArchitectureDeliverable deliverable in plan.BackendFiles)
        {
            if (string.IsNullOrWhiteSpace(deliverable.Path))
            {
                continue;
            }

            string normalized = NormalizePath(deliverable.Path);
            string stem = Path.GetFileNameWithoutExtension(Path.GetFileName(normalized));
            if (!DeliverableFileNameSupport.TryGetRoleSuffix(stem, out string roleSuffix))
            {
                continue;
            }

            if (!foldersByRoleSuffix.TryGetValue(roleSuffix, out HashSet<string>? allowedFolders)
                || allowedFolders.Count == 0)
            {
                continue;
            }

            string? parentDirectory = Path.GetDirectoryName(normalized)?.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(parentDirectory))
            {
                continue;
            }

            if (allowedFolders.Contains(parentDirectory))
            {
                continue;
            }

            yield return new AgentFinding
            {
                Severity = FindingSeverity.High,
                Message =
                    $"Architecture plan path '{normalized}' is not under an on-disk folder used by same-kind exemplars. "
                    + $"Use a parent folder from RAG 'Grouped folder exemplars' "
                    + $"({string.Join(", ", allowedFolders.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))})."
            };
        }
    }

    internal static IReadOnlyDictionary<string, HashSet<string>> DiscoverParentFoldersByRoleSuffix(string repoPath)
    {
        var foldersByRole = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (string relativePath in DiscoverProductionRelativePaths(repoPath))
        {
            string stem = Path.GetFileNameWithoutExtension(Path.GetFileName(relativePath));
            if (!DeliverableFileNameSupport.TryGetRoleSuffix(stem, out string roleSuffix))
            {
                continue;
            }

            string? parent = Path.GetDirectoryName(relativePath)?.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(parent))
            {
                continue;
            }

            if (!foldersByRole.TryGetValue(roleSuffix, out HashSet<string>? folders))
            {
                folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foldersByRole[roleSuffix] = folders;
            }

            folders.Add(parent);
        }

        return foldersByRole;
    }

    internal static IReadOnlyList<string> DiscoverProductionRelativePaths(string repoPath)
    {
        if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
        {
            return Array.Empty<string>();
        }

        string repoRoot = Path.GetFullPath(repoPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var paths = new List<string>();

        foreach (string absolute in Directory.EnumerateFiles(repoPath, "*.cs", SearchOption.AllDirectories))
        {
            string normalized = absolute.Replace('\\', '/');
            if (normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string relative = Path.GetRelativePath(repoRoot, absolute).Replace('\\', '/');
            if (relative.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase)
                || relative.EndsWith(".AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            paths.Add(relative);
        }

        return paths;
    }

    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> GroupPathsByParentFolder(
        IReadOnlyList<string> relativePaths)
    {
        var groups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in relativePaths)
        {
            string? parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(parent))
            {
                continue;
            }

            if (!groups.TryGetValue(parent, out List<string>? list))
            {
                list = new List<string>();
                groups[parent] = list;
            }

            list.Add(path);
        }

        return groups.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<string>)kvp.Value.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList(),
            StringComparer.OrdinalIgnoreCase);
    }

    internal static string NormalizePath(string path) =>
        path.Replace('\\', '/').TrimStart('/');
}
