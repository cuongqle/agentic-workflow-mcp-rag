using System.Text.RegularExpressions;

namespace agents_mcp_rag.Infrastructure;

internal static class SolutionProjectCatalog
{
    public static string? FindPrimarySolutionPath(string repoPath)
    {
        return Directory
            .EnumerateFiles(repoPath, "*.sln", SearchOption.AllDirectories)
            .Where(path => !IsUnderArtifactPath(path))
            .OrderBy(path => path.Length)
            .FirstOrDefault();
    }

    public static IReadOnlyList<string> GetSolutionProjectRelativePaths(string repoPath, string? solutionPath = null)
    {
        solutionPath ??= FindPrimarySolutionPath(repoPath);
        if (string.IsNullOrWhiteSpace(solutionPath) || !File.Exists(solutionPath))
        {
            return Array.Empty<string>();
        }

        string solutionDirectory = (Path.GetDirectoryName(solutionPath) ?? repoPath).Replace('\\', '/');
        string repoRoot = Path.GetFullPath(repoPath).Replace('\\', '/').TrimEnd('/');
        var projects = new List<string>();

        foreach (string line in File.ReadAllLines(solutionPath))
        {
            Match match = Regex.Match(
                line,
                @"Project\s*\([^)]+\)\s*=\s*""[^""]+""\s*,\s*""([^""]+\.csproj)""",
                RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                continue;
            }

            string projectRelative = match.Groups[1].Value.Replace('\\', '/');
            string absolute = Path.GetFullPath(Path.Combine(solutionDirectory, projectRelative))
                .Replace('\\', '/');
            if (!absolute.StartsWith(repoRoot + "/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            projects.Add(absolute[(repoRoot.Length + 1)..]);
        }

        return projects
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool IsSolutionProject(string repoPath, string projectRelativePath)
    {
        string normalized = projectRelativePath.Replace('\\', '/').TrimStart('/');
        return GetSolutionProjectRelativePaths(repoPath)
            .Any(path => path.Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsUnderArtifactPath(string path)
    {
        string normalized = path.Replace('\\', '/');
        return normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase);
    }
}
