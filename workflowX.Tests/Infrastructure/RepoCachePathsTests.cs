using workflowX.Infrastructure;

namespace workflowX.Tests.Infrastructure;

public class RepoCachePathsTests
{
    [Fact]
    public void GetDefaultCacheRoot_UsesHomeDirectory()
    {
        string expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".workflowx",
            "repo-cache");

        Assert.Equal(expected, RepoCachePaths.GetDefaultCacheRoot());
    }

    [Fact]
    public void ResolveCacheRoot_UsesConfiguredPath()
    {
        string configured = Path.Combine(Path.GetTempPath(), "workflowx-custom-cache");

        string resolved = RepoCachePaths.ResolveCacheRoot(configured);

        Assert.Equal(Path.GetFullPath(configured), resolved);
    }

    [Fact]
    public void ResolveCacheRoot_ExpandsTilde()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string expected = Path.Combine(home, ".workflowx", "repo-cache");

        string resolved = RepoCachePaths.ResolveCacheRoot("~/.workflowx/repo-cache");

        Assert.Equal(expected, resolved);
    }

    [Fact]
    public void BuildRepoCacheFolderName_IsStableForSameUrl()
    {
        const string repoUrl = "https://github.com/org/my-app.git";

        string first = RepoCachePaths.BuildRepoCacheFolderName(repoUrl);
        string second = RepoCachePaths.BuildRepoCacheFolderName(repoUrl);

        Assert.Equal(first, second);
        Assert.StartsWith("my-app-", first, StringComparison.Ordinal);
    }

    [Fact]
    public void TryMigrateLegacyCheckout_MovesCloneToPreferredCache()
    {
        const string repoUrl = "https://github.com/org/migrate-me.git";
        string folderName = RepoCachePaths.BuildRepoCacheFolderName(repoUrl);
        string legacyRoot = Path.Combine(Path.GetTempPath(), $"workflowx-legacy-{Guid.NewGuid():N}");
        string preferredRoot = Path.Combine(Path.GetTempPath(), $"workflowx-new-{Guid.NewGuid():N}");
        string legacyPath = Path.Combine(legacyRoot, folderName);
        string preferredPath = Path.Combine(preferredRoot, folderName);

        try
        {
            Directory.CreateDirectory(Path.Combine(legacyPath, ".git"));

            string? migratedFrom = RepoCachePaths.TryMigrateLegacyCheckout(
                repoUrl,
                configuredCachePath: preferredRoot,
                preferredPath: preferredPath,
                legacyRootOverride: legacyRoot);

            Assert.Equal(legacyPath, migratedFrom);
            Assert.True(RepoCachePaths.HasGitCheckout(preferredPath));
            Assert.False(Directory.Exists(legacyPath));
        }
        finally
        {
            if (Directory.Exists(legacyRoot))
            {
                Directory.Delete(legacyRoot, recursive: true);
            }

            if (Directory.Exists(preferredRoot))
            {
                Directory.Delete(preferredRoot, recursive: true);
            }
        }
    }
}
