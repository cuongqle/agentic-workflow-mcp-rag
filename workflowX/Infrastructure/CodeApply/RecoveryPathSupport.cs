namespace workflowX.Infrastructure;

/// <summary>
/// Normalizes recovery paths that incorrectly repeat the repository or solution folder segment.
/// </summary>
internal static class RecoveryPathSupport
{
    internal static string CanonicalizeRecoveryPath(string relativePath)
    {
        string path = NormalizePath(relativePath);
        bool changed;
        do
        {
            changed = false;
            if (TryStripDuplicateRepositoryFolderPrefix(path, out string slashFixed)
                && !slashFixed.Equals(path, StringComparison.OrdinalIgnoreCase))
            {
                path = slashFixed;
                changed = true;
            }

            if (TryStripDuplicateRepositoryDotPrefix(path, out string dotFixed)
                && !dotFixed.Equals(path, StringComparison.OrdinalIgnoreCase))
            {
                path = dotFixed;
                changed = true;
            }
        }
        while (changed);

        return path;
    }

    internal static bool TryStripDuplicateRepositoryFolderPrefix(string relativePath, out string canonicalPath)
    {
        canonicalPath = NormalizePath(relativePath);
        int slash = canonicalPath.IndexOf('/');
        if (slash <= 0)
        {
            return false;
        }

        string firstSegment = canonicalPath[..slash];
        string remainder = canonicalPath[(slash + 1)..];
        if (!remainder.StartsWith(firstSegment + ".", StringComparison.OrdinalIgnoreCase)
            && !remainder.StartsWith(firstSegment + "/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // ProjectFolder/ProjectFolder.csproj is normal layout — not a duplicated repository folder.
        if (!remainder.Contains('/')
            && remainder.Equals(firstSegment + ".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        canonicalPath = remainder;
        return true;
    }

    internal static bool TryStripDuplicateRepositoryDotPrefix(string relativePath, out string canonicalPath)
    {
        canonicalPath = NormalizePath(relativePath);
        int slash = canonicalPath.IndexOf('/');
        string head = slash > 0 ? canonicalPath[..slash] : canonicalPath;
        string tail = slash > 0 ? canonicalPath[(slash + 1)..] : string.Empty;

        string[] segments = head.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2
            || !segments[0].Equals(segments[1], StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string fixedHead = string.Join('.', segments.Skip(1));
        canonicalPath = string.IsNullOrWhiteSpace(tail) ? fixedHead : $"{fixedHead}/{tail}";
        return !canonicalPath.Equals(NormalizePath(relativePath), StringComparison.OrdinalIgnoreCase);
    }

    internal static bool HasDuplicateRepositoryFolderPrefix(string relativePath) =>
        !CanonicalizeRecoveryPath(relativePath)
            .Equals(NormalizePath(relativePath), StringComparison.OrdinalIgnoreCase);

    internal static string NormalizePath(string path) =>
        path.Replace('\\', '/').TrimStart('/');
}
