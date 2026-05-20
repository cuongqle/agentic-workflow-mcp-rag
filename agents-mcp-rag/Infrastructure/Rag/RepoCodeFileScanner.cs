static class RepoCodeFileScanner
{
    private static readonly HashSet<string> IncludedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".js", ".ts", ".tsx", ".jsx", ".html", ".css", ".scss",
        ".json", ".csproj", ".sln", ".md"
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
        "/agents-mcp-rag-output/"
    };

    public static IEnumerable<string> EnumerateRelevantFiles(string repoPath)
    {
        if (!Directory.Exists(repoPath))
        {
            return Enumerable.Empty<string>();
        }

        return Directory
            .EnumerateFiles(repoPath, "*.*", SearchOption.AllDirectories)
            .Where(IsRelevantFile)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsRelevantFile(string absolutePath)
    {
        string normalized = absolutePath.Replace('\\', '/');
        if (ExcludedPathTokens.Any(token => normalized.Contains(token, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (!IncludedExtensions.Contains(Path.GetExtension(absolutePath)))
        {
            return false;
        }

        var info = new FileInfo(absolutePath);
        return info.Exists && info.Length <= 512 * 1024;
    }
}
