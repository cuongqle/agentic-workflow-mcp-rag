namespace workflowX.Infrastructure;

static class RepoCodeFileScanner
{
    private static readonly HashSet<string> CommonExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".js", ".ts", ".tsx", ".jsx", ".html", ".vue", ".css", ".scss",
        ".json", ".md"
    };

    private static readonly HashSet<string> DotNetExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".csproj", ".sln"
    };

    private static readonly string[] ExcludedPathTokens =
    {
        "/.git/",
        "/.idea/",
        "/.vs/",
        "/bin/",
        "/obj/",
        "/node_modules/",
        "/dist/",
        "/build/",
        "/coverage/",
        "/workflowX-output/"
    };

    public static IEnumerable<string> EnumerateRelevantFiles(string repoPath, RepoContract? contract = null)
    {
        if (!Directory.Exists(repoPath))
        {
            return Enumerable.Empty<string>();
        }

        var allowedExtensions = ResolveAllowedExtensions(contract);

        return Directory
            .EnumerateFiles(repoPath, "*.*", SearchOption.AllDirectories)
            .Where(path => IsRelevantFile(path, allowedExtensions))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<string> ResolveAllowedExtensions(RepoContract? contract)
    {
        var allowed = new HashSet<string>(CommonExtensions, StringComparer.OrdinalIgnoreCase);

        // No contract yet (early RAG build): include all known stack extensions.
        if (contract is null)
        {
            allowed.UnionWith(DotNetExtensions);
            return allowed;
        }

        contract.Stack.WhenDotNet(() => allowed.UnionWith(DotNetExtensions));

        return allowed;
    }

    private static bool IsRelevantFile(string absolutePath, HashSet<string> allowedExtensions)
    {
        string normalized = absolutePath.Replace('\\', '/');
        if (ExcludedPathTokens.Any(token => normalized.Contains(token, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (!allowedExtensions.Contains(Path.GetExtension(absolutePath)))
        {
            return false;
        }

        var info = new FileInfo(absolutePath);
        return info.Exists && info.Length <= 512 * 1024;
    }
}
