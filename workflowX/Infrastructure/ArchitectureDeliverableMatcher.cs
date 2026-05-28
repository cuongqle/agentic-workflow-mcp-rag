namespace workflowX.Infrastructure;

/// <summary>
/// Shared path matching for architecture deliverables (plan paths vs agent output paths).
/// </summary>
internal static class ArchitectureDeliverableMatcher
{
    internal static IReadOnlyList<string> BuildBackendAllowedPaths(WorkflowState state)
    {
        var paths = new List<string>();
        paths.AddRange(WorkflowFindingRules.GetBackendPaths(state));
        paths.AddRange(MissingLayerTestSynthesizer.GetRequiredTestPaths(state));
        return paths;
    }

    internal static IReadOnlyList<string> BuildFrontendAllowedPaths(WorkflowState state) =>
        WorkflowFindingRules.GetFrontendPaths(state);

    internal static bool IsAllowedDeliverable(
        string relativePath,
        IReadOnlyList<string> allowedPaths,
        RepoContract? contract = null)
    {
        if (allowedPaths.Count == 0)
        {
            return true;
        }

        string normalized = NormalizePath(relativePath);
        foreach (string allowed in allowedPaths)
        {
            if (PathsMatch(normalized, allowed))
            {
                return true;
            }
        }

        DeliverableCompanionContext companionContext = DeliverableCompanionContext.Create(contract, allowedPaths);
        return companionContext.IsCompanion(normalized);
    }

    internal static bool PathsMatch(string proposedRelativePath, string requiredPath)
    {
        string proposed = NormalizePath(proposedRelativePath);
        string required = NormalizePath(requiredPath);
        if (proposed.Equals(required, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (proposed.EndsWith("/" + required, StringComparison.OrdinalIgnoreCase)
            || required.EndsWith("/" + proposed, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!Path.GetFileName(proposed).Equals(Path.GetFileName(required), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string? proposedDir = Path.GetDirectoryName(proposed)?.Replace('\\', '/');
        string? requiredDir = Path.GetDirectoryName(required)?.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(proposedDir) || string.IsNullOrWhiteSpace(requiredDir))
        {
            return true;
        }

        return DirectoryPathsCompatible(proposedDir, requiredDir);
    }

    internal static bool IsNearPlannedDeliverable(string normalizedPath, IReadOnlyList<string> plannedPaths)
    {
        string? directory = Path.GetDirectoryName(normalizedPath)?.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        foreach (string planned in plannedPaths)
        {
            string plannedNormalized = NormalizePath(planned);
            string? plannedDirectory = Path.GetDirectoryName(plannedNormalized)?.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(plannedDirectory))
            {
                continue;
            }

            if (directory.Equals(plannedDirectory, StringComparison.OrdinalIgnoreCase)
                || directory.StartsWith(plannedDirectory + "/", StringComparison.OrdinalIgnoreCase)
                || plannedDirectory.StartsWith(directory + "/", StringComparison.OrdinalIgnoreCase)
                || DirectoryPathsCompatible(directory, plannedDirectory))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool DirectoryPathsCompatible(string proposedDir, string requiredDir)
    {
        if (string.IsNullOrWhiteSpace(proposedDir) || string.IsNullOrWhiteSpace(requiredDir))
        {
            return false;
        }

        string[] proposedSegments = proposedDir.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string[] requiredSegments = requiredDir.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (proposedSegments.Any(segment => requiredSegments.Contains(segment, StringComparer.OrdinalIgnoreCase)))
        {
            return true;
        }

        int maxOverlap = Math.Min(proposedSegments.Length, requiredSegments.Length);
        for (int overlap = maxOverlap; overlap >= 1; overlap--)
        {
            bool match = true;
            for (int i = 0; i < overlap; i++)
            {
                if (!proposedSegments[proposedSegments.Length - overlap + i]
                        .Equals(requiredSegments[requiredSegments.Length - overlap + i], StringComparison.OrdinalIgnoreCase))
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return true;
            }
        }

        return false;
    }

    internal static bool PathUnderDirectory(string normalizedPath, string directory)
    {
        directory = directory.Replace('\\', '/').Trim().Trim('/');
        if (normalizedPath.Equals(directory, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (normalizedPath.StartsWith(directory + "/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (normalizedPath.Contains("/" + directory + "/", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.EndsWith("/" + directory, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string? parent = Path.GetDirectoryName(normalizedPath)?.Replace('\\', '/');
        return !string.IsNullOrWhiteSpace(parent) && DirectoryPathsCompatible(parent, directory);
    }

    internal static string NormalizePath(string path) =>
        path.Replace('\\', '/').Trim().TrimStart('/');
}
