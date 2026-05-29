using System.Text;

namespace workflowX.Infrastructure.Exemplar.DotNet;

/// <summary>
/// Inlines full on-disk exemplar sources for implement agents (same filename role pattern as planned paths).
/// </summary>
internal static class ImplementationExemplarSupport
{
    private const int DefaultMaxChars = 32_000;
    private const int MaxExemplarsPerPlannedPath = 3;

    internal static string BuildContext(WorkflowState state, int maxTotalChars = DefaultMaxChars)
    {
        if (string.IsNullOrWhiteSpace(state.RepoPath) || !Directory.Exists(state.RepoPath))
        {
            return string.Empty;
        }

        IReadOnlyList<string> plannedPaths = GetPlannedBackendPaths(state);
        if (plannedPaths.Count == 0)
        {
            return string.Empty;
        }

        IReadOnlyList<string> productionPaths = ProductionPathExemplarSupport.DiscoverProductionRelativePaths(state.RepoPath);
        IReadOnlyList<string> testPaths = ExemplarTestCompanionSupport.DiscoverTestRelativePaths(state.RepoPath);
        if (productionPaths.Count == 0 && testPaths.Count == 0)
        {
            return string.Empty;
        }

        var attached = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sb = new StringBuilder();
        int usedChars = 0;

        EntityConvention? entityConvention = state.Contract?.Entity;
        if (entityConvention is not null
            && !string.IsNullOrWhiteSpace(entityConvention.ExemplarRelativePath))
        {
            usedChars = TryAttachFile(
                state.RepoPath,
                entityConvention.ExemplarRelativePath,
                attached,
                sb,
                usedChars,
                maxTotalChars);
        }

        foreach (string plannedPath in plannedPaths)
        {
            string? plannedParent = GetParentDirectory(plannedPath);
            IEnumerable<string> exemplarPaths = TryGetRoleSuffixFromPath(plannedPath, out string roleSuffix)
                ? SelectSameKindExemplars(productionPaths, plannedPath, plannedParent, roleSuffix)
                : SelectSameFolderExemplars(productionPaths, plannedPath, plannedParent);

            foreach (string exemplarPath in exemplarPaths)
            {
                usedChars = TryAttachFile(state.RepoPath, exemplarPath, attached, sb, usedChars, maxTotalChars);
                if (usedChars < 0)
                {
                    return sb.ToString().TrimEnd();
                }
            }

            if (TryGetRoleSuffixFromPath(plannedPath, out _))
            {
                foreach (string companionExemplar in ExemplarRoleCompanionSupport.DiscoverCompanionExemplarPaths(
                             state.RepoPath,
                             plannedPath,
                             productionPaths))
                {
                    usedChars = TryAttachFile(state.RepoPath, companionExemplar, attached, sb, usedChars, maxTotalChars);
                    if (usedChars < 0)
                    {
                        return sb.ToString().TrimEnd();
                    }
                }
            }

            if (plannedPath.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase))
            {
                string? testExemplar = ExemplarTestCompanionSupport.DiscoverTestExemplarPath(
                    state.RepoPath,
                    plannedPath,
                    productionPaths,
                    testPaths);
                if (!string.IsNullOrWhiteSpace(testExemplar))
                {
                    usedChars = TryAttachFile(state.RepoPath, testExemplar, attached, sb, usedChars, maxTotalChars);
                    if (usedChars < 0)
                    {
                        return sb.ToString().TrimEnd();
                    }
                }
            }
        }

        string usingHints = ExemplarUsingSupport.BuildImplementationUsingHints(
            state.RepoPath,
            plannedPaths,
            productionPaths);
        if (!string.IsNullOrWhiteSpace(usingHints))
        {
            sb.AppendLine();
            sb.AppendLine("Required usings and namespaces (from exemplar sources — copy exactly):");
            sb.AppendLine(usingHints);
        }

        return sb.Length == 0 ? string.Empty : sb.ToString().TrimEnd();
    }

    private static IReadOnlyList<string> GetPlannedBackendPaths(WorkflowState state)
    {
        if (state.ArchitecturePlan?.BackendFiles is { Count: > 0 } deliverables)
        {
            return deliverables
                .Select(d => ProductionPathExemplarSupport.NormalizePath(d.Path))
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return Array.Empty<string>();
    }

    private static int TryAttachFile(
        string repoPath,
        string exemplarPath,
        HashSet<string> attached,
        StringBuilder sb,
        int usedChars,
        int maxTotalChars)
    {
        if (!attached.Add(exemplarPath))
        {
            return usedChars;
        }

        string absolute = Path.Combine(repoPath, exemplarPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(absolute))
        {
            return usedChars;
        }

        string block = FormatFileBlock(exemplarPath, File.ReadAllText(absolute));
        if (usedChars > 0 && usedChars + block.Length > maxTotalChars)
        {
            sb.AppendLine();
            sb.AppendLine(
                $"[Additional same-kind exemplars omitted — {attached.Count} file(s) attached; mirror patterns from these.]");
            return -1;
        }

        sb.Append(block);
        return usedChars + block.Length;
    }

    private static IEnumerable<string> SelectSameFolderExemplars(
        IReadOnlyList<string> productionPaths,
        string plannedPath,
        string? plannedParent)
    {
        if (string.IsNullOrWhiteSpace(plannedParent))
        {
            return Array.Empty<string>();
        }

        return productionPaths
            .Where(path => !path.Equals(plannedPath, StringComparison.OrdinalIgnoreCase))
            .Where(path => SharesParentDirectory(path, plannedParent))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Take(MaxExemplarsPerPlannedPath);
    }

    private static IEnumerable<string> SelectSameKindExemplars(
        IReadOnlyList<string> productionPaths,
        string plannedPath,
        string? plannedParent,
        string roleSuffix)
    {
        return productionPaths
            .Where(path => !path.Equals(plannedPath, StringComparison.OrdinalIgnoreCase))
            .Where(path => TryGetRoleSuffixFromPath(path, out string suffix)
                && suffix.Equals(roleSuffix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(path => SharesParentDirectory(path, plannedParent))
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Take(MaxExemplarsPerPlannedPath);
    }

    private static bool TryGetRoleSuffixFromPath(string relativePath, out string roleSuffix)
    {
        roleSuffix = string.Empty;
        string stem = Path.GetFileNameWithoutExtension(Path.GetFileName(relativePath));
        return DeliverableFileNameSupport.TryGetRoleSuffix(stem, out roleSuffix);
    }

    private static string? GetParentDirectory(string relativePath) =>
        Path.GetDirectoryName(relativePath.Replace('\\', '/'))?.Replace('\\', '/');

    private static bool SharesParentDirectory(string path, string? parentDirectory) =>
        !string.IsNullOrWhiteSpace(parentDirectory)
        && parentDirectory.Equals(GetParentDirectory(path), StringComparison.OrdinalIgnoreCase);

    private static string FormatFileBlock(string relativePath, string content) =>
        $"\n--- FILE: {relativePath} ---\n{content.TrimEnd()}\n--- END {relativePath} ---\n";

}
