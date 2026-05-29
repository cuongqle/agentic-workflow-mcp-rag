namespace workflowX.Infrastructure;

/// <summary>
/// Shared path matching for architecture deliverables (plan paths vs agent output paths).
/// </summary>
internal static class ArchitectureDeliverableMatcher
{
    internal static IReadOnlyList<string> BuildBackendAllowedPaths(WorkflowState state)
    {
        return WorkflowFindingRules.GetBackendPaths(state);
    }

    internal static IReadOnlyList<string> BuildFrontendAllowedPaths(WorkflowState state) =>
        WorkflowFindingRules.GetFrontendPaths(state);

    /// <summary>
    /// Strict allow-list check for implement stage: exact planned path or planned I* interface companion only.
    /// </summary>
    internal static bool IsStrictArchitectureDeliverable(
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
            if (IsExactPlannedPath(normalized, NormalizePath(allowed)))
            {
                return true;
            }
        }

        return IsPlannedInterfaceCompanion(normalized, allowedPaths);
    }

    internal static bool IsAllowedDeliverable(
        string relativePath,
        IReadOnlyList<string> allowedPaths,
        RepoContract? contract = null)
    {
        if (allowedPaths.Count == 0)
        {
            return true;
        }

        if (IsStrictArchitectureDeliverable(relativePath, allowedPaths, contract))
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

    internal static bool IsExactPlannedPath(string proposed, string planned) =>
        proposed.Equals(planned, StringComparison.OrdinalIgnoreCase);

    internal static bool IsPlannedInterfaceCompanion(string normalizedPath, IReadOnlyList<string> allowedPaths)
    {
        string fileName = Path.GetFileName(normalizedPath);
        string extension = Path.GetExtension(fileName);
        string stem = Path.GetFileNameWithoutExtension(fileName);
        if (!stem.StartsWith('I') || stem.Length <= 1)
        {
            return false;
        }

        string implementationFileName = stem[1..] + extension;
        foreach (string allowed in allowedPaths)
        {
            string allowedFileName = Path.GetFileName(allowed);
            if (allowedFileName.Equals(implementationFileName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (allowedFileName.StartsWith("I", StringComparison.Ordinal)
                && allowedFileName.Length > 1
                && allowedFileName[1..].Equals(implementationFileName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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

        if (proposedDir.Equals(requiredDir, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (proposedDir.EndsWith("/" + requiredDir, StringComparison.OrdinalIgnoreCase)
            || requiredDir.EndsWith("/" + proposedDir, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string[] proposedSegments = proposedDir.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string[] requiredSegments = requiredDir.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string[] shorter = proposedSegments.Length <= requiredSegments.Length ? proposedSegments : requiredSegments;
        string[] longer = proposedSegments.Length > requiredSegments.Length ? proposedSegments : requiredSegments;
        if (shorter.Length == 0 || longer.Length < shorter.Length)
        {
            return false;
        }

        bool suffixMatch = true;
        for (int i = 0; i < shorter.Length; i++)
        {
            if (!longer[longer.Length - shorter.Length + i]
                    .Equals(shorter[i], StringComparison.OrdinalIgnoreCase))
            {
                suffixMatch = false;
                break;
            }
        }

        if (suffixMatch)
        {
            return true;
        }

        return SharesSolutionProjectSegment(proposedDir, requiredDir);
    }

    private static bool SharesSolutionProjectSegment(string proposedDir, string requiredDir)
    {
        var proposedProjects = ExtractSolutionProjectSegments(proposedDir);
        var requiredProjects = ExtractSolutionProjectSegments(requiredDir);
        return proposedProjects.Any(segment => requiredProjects.Contains(segment, StringComparer.OrdinalIgnoreCase));
    }

    private static HashSet<string> ExtractSolutionProjectSegments(string directory)
    {
        return directory
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(segment => segment.Contains('.', StringComparison.Ordinal))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
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
