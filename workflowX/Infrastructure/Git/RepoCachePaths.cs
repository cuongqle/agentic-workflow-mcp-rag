namespace workflowX.Infrastructure;

/// <summary>
/// Resolves where remote repositories are cloned locally.
/// </summary>
public static class RepoCachePaths
{
    public const string EnvironmentVariableName = "WORKFLOWX_REPO_CACHE";
    private const string ReadmeFileName = "README.txt";

    public static string GetDefaultCacheRoot() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".workflowx",
            "repo-cache");

    public static string? GetLegacyCacheRoot()
    {
        string legacy = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "workflowX",
            "repo-cache");

        return Directory.Exists(legacy) ? legacy : null;
    }

    public static string ResolveCacheRoot(string? configuredCachePath)
    {
        if (!string.IsNullOrWhiteSpace(configuredCachePath))
        {
            return Path.GetFullPath(Expand(configuredCachePath));
        }

        string? fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return Path.GetFullPath(Expand(fromEnvironment));
        }

        return GetDefaultCacheRoot();
    }

    public static string ResolveWorkingCopyPath(string configuredRepoPath, string? configuredCachePath) =>
        ResolveWorkingCopy(configuredRepoPath, configuredCachePath).Path;

    public static (string Path, string? MigratedFrom) ResolveWorkingCopy(
        string configuredRepoPath,
        string? configuredCachePath)
    {
        string folderName = BuildRepoCacheFolderName(configuredRepoPath);
        string cacheRoot = ResolveCacheRoot(configuredCachePath);
        string preferredPath = Path.Combine(cacheRoot, folderName);
        if (HasGitCheckout(preferredPath))
        {
            return (preferredPath, null);
        }

        string? migratedFrom = TryMigrateLegacyCheckout(
            configuredRepoPath,
            configuredCachePath,
            preferredPath);
        return (preferredPath, migratedFrom);
    }

    /// <summary>
    /// Moves an existing clone from the legacy Application Support cache into the configured cache root.
    /// </summary>
    public static string? TryMigrateLegacyCheckout(
        string configuredRepoPath,
        string? configuredCachePath,
        string preferredPath,
        string? legacyRootOverride = null)
    {
        if (HasGitCheckout(preferredPath))
        {
            return null;
        }

        string cacheRoot = ResolveCacheRoot(configuredCachePath);
        string? legacyRoot = legacyRootOverride ?? GetLegacyCacheRoot();
        if (legacyRoot is null || string.Equals(legacyRoot, cacheRoot, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string legacyPath = Path.Combine(legacyRoot, BuildRepoCacheFolderName(configuredRepoPath));
        if (!HasGitCheckout(legacyPath))
        {
            return null;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(preferredPath)!);
        if (Directory.Exists(preferredPath))
        {
            Directory.Delete(preferredPath, recursive: true);
        }

        try
        {
            Directory.Move(legacyPath, preferredPath);
            return legacyPath;
        }
        catch (IOException)
        {
            CopyDirectory(legacyPath, preferredPath);
            Directory.Delete(legacyPath, recursive: true);
            return legacyPath;
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(directory.Replace(source, destination, StringComparison.Ordinal));
        }

        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string targetFile = file.Replace(source, destination, StringComparison.Ordinal);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            File.Copy(file, targetFile, overwrite: true);
        }
    }

    public static void EnsureCacheRoot(string cacheRoot)
    {
        Directory.CreateDirectory(cacheRoot);
        string readmePath = Path.Combine(cacheRoot, ReadmeFileName);
        if (File.Exists(readmePath))
        {
            return;
        }

        File.WriteAllText(
            readmePath,
            """
            workflowX remote repository cache
            =================================

            Local clones of remote repositories configured as Repo.Path in appsettings.json.

            Default location:
              ~/.workflowx/repo-cache

            Override:
              appsettings.json  ->  "Repo": { "CachePath": "/your/path" }
              environment       ->  WORKFLOWX_REPO_CACHE=/your/path

            Each subdirectory is named after the repository URL (for example: my-app-a1b2c3d4).
            """);
    }

    public static string BuildRepoCacheFolderName(string repoUrl)
    {
        string trimmed = repoUrl.TrimEnd('/');
        string lastSegment = trimmed.Split('/').LastOrDefault() ?? "repo";
        if (lastSegment.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            lastSegment = lastSegment[..^4];
        }

        string safeName = string.Concat(lastSegment.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(repoUrl)))[..8].ToLowerInvariant();
        return $"{safeName}-{hash}";
    }

    public static bool HasGitCheckout(string path) =>
        Directory.Exists(path) && Directory.Exists(Path.Combine(path, ".git"));

    private static string Expand(string path)
    {
        string trimmed = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        if (trimmed == "~")
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        if (trimmed.StartsWith("~/", StringComparison.Ordinal) || trimmed.StartsWith("~\\", StringComparison.Ordinal))
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, trimmed[2..]);
        }

        return trimmed;
    }
}
