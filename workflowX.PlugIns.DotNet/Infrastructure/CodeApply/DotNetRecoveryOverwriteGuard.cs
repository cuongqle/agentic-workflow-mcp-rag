namespace workflowX.Infrastructure.CodeApply.DotNet;

/// <summary>
/// During .NET recovery, blocks overwriting on-disk files not in the allowed path set (compiler output, planned tests, etc.).
/// Caller must pass repo-relative paths already canonicalized (duplicate repo-folder prefixes stripped).
/// </summary>
internal static class DotNetRecoveryOverwriteGuard
{
    internal static bool TryValidateOverwrite(
        string repoPath,
        string canonicalRelativePath,
        bool existedBefore,
        IReadOnlySet<string> allowedPaths,
        out string reason)
    {
        reason = string.Empty;
        if (!existedBefore)
        {
            return true;
        }

        if (IsAllowedOverwrite(repoPath, canonicalRelativePath, allowedPaths))
        {
            return true;
        }

        reason =
            $"Recovery rejected overwrite of '{canonicalRelativePath}': no compiler error was reported for this file. "
            + "Edit only files referenced in build findings (use exact repo-relative paths — do not duplicate the repository folder name).";
        return false;
    }

    internal static bool IsAllowedOverwrite(
        string repoPath,
        string canonicalRelativePath,
        IReadOnlySet<string> allowedPaths)
    {
        if (allowedPaths.Contains(canonicalRelativePath))
        {
            return true;
        }

        if (!canonicalRelativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (string errorPath in allowedPaths)
        {
            if (!errorPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? owningCsproj = TestProjectPathSupport.TryResolveOwningProjectCsproj(repoPath, errorPath)
                ?? TestProjectPathSupport.TryResolveOwningTestCsproj(repoPath, errorPath);
            if (string.IsNullOrWhiteSpace(owningCsproj))
            {
                continue;
            }

            if (owningCsproj.Equals(canonicalRelativePath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
