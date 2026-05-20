using System.Security.Cryptography;
using System.Text;

namespace agents_mcp_rag.Infrastructure;

public static class RepositoryResolver
{
    public static string Prepare(string configuredRepoPath)
    {
        if (!IsRemoteRepository(configuredRepoPath))
        {
            return configuredRepoPath;
        }

        string cacheRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "agents-mcp-rag",
            "repo-cache");
        Directory.CreateDirectory(cacheRoot);

        string localPath = Path.Combine(cacheRoot, BuildRepoCacheFolderName(configuredRepoPath));
        if (Directory.Exists(localPath) && Directory.Exists(Path.Combine(localPath, ".git")))
        {
            Console.WriteLine($"Refreshing cached repository: {configuredRepoPath}");
            GitCommandRunner.Run($"-C \"{localPath}\" pull --ff-only");
        }
        else
        {
            if (Directory.Exists(localPath))
            {
                Directory.Delete(localPath, recursive: true);
            }

            Console.WriteLine($"Cloning remote repository: {configuredRepoPath}");
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

    private static string BuildRepoCacheFolderName(string repoUrl)
    {
        string trimmed = repoUrl.TrimEnd('/');
        string lastSegment = trimmed.Split('/').LastOrDefault() ?? "repo";
        if (lastSegment.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            lastSegment = lastSegment[..^4];
        }

        string safeName = string.Concat(lastSegment.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(repoUrl)))[..8].ToLowerInvariant();
        return $"{safeName}-{hash}";
    }

}
