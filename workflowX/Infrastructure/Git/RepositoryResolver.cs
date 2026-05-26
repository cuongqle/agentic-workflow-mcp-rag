namespace workflowX.Infrastructure;

public static class RepositoryResolver
{
    public static string Prepare(string configuredRepoPath, string? configuredCachePath = null)
    {
        if (!IsRemoteRepository(configuredRepoPath))
        {
            string resolvedLocalRepo = Path.GetFullPath(configuredRepoPath);
            Console.WriteLine($"Using local repository: {resolvedLocalRepo}");
            return resolvedLocalRepo;
        }

        string cacheRoot = RepoCachePaths.ResolveCacheRoot(configuredCachePath);
        RepoCachePaths.EnsureCacheRoot(cacheRoot);

        (string localPath, string? migratedFrom) = RepoCachePaths.ResolveWorkingCopy(
            configuredRepoPath,
            configuredCachePath);

        Console.WriteLine();
        Console.WriteLine("=== Repository ===");
        Console.WriteLine($"Remote URL:     {configuredRepoPath}");
        Console.WriteLine($"Cache root:     {cacheRoot}");
        if (migratedFrom is not null)
        {
            Console.WriteLine($"Migrated from:  {migratedFrom}");
        }

        Console.WriteLine($"Working copy:   {localPath}");
        Console.WriteLine();

        if (RepoCachePaths.HasGitCheckout(localPath))
        {
            Console.WriteLine("Refreshing cached repository...");
            GitCommandRunner.Run($"-C \"{localPath}\" pull --ff-only");
        }
        else
        {
            if (Directory.Exists(localPath))
            {
                Directory.Delete(localPath, recursive: true);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
            Console.WriteLine("Cloning remote repository into cache...");
            GitCommandRunner.Run($"clone --depth 1 \"{configuredRepoPath}\" \"{localPath}\"");
        }

        return localPath;
    }

    private static bool IsRemoteRepository(string path)
    {
        return path.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("git@", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase);
    }
}
