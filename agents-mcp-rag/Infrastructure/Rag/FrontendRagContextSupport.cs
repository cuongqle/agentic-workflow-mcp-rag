using System.Text;

namespace agents_mcp_rag.Infrastructure;

/// <summary>
/// Frontend (JS/TS/HTML) RAG context — only invoked when <see cref="RepoStack.Frontend"/>.
/// </summary>
internal static class FrontendRagContextSupport
{
    internal static void AppendImplementationContext(
        StringBuilder sb,
        IReadOnlyList<string> rankedPaths,
        RepoContract contract,
        string repoPath)
    {
        AppendCategory(sb, "Frontend UI modules", rankedPaths, path => IsFrontendModulePath(path, contract), repoPath);
        AppendCategory(sb, "Frontend tests", rankedPaths, path =>
            path.Contains(".spec.", StringComparison.OrdinalIgnoreCase)
            || path.Contains(".test.", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("Tests.js", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("Tests.ts", StringComparison.OrdinalIgnoreCase), repoPath);
    }

    internal static IEnumerable<string> SemanticQueries()
    {
        yield return "angular module controller service view proxy patterns";
        yield return "frontend component routing bootstrap loader";
    }

    internal static int ScoreFrontendPath(string normalizedPath, FrontendModuleTemplate frontend)
    {
        int score = 0;
        if (normalizedPath.StartsWith(frontend.ModulesRoot + "/", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(frontend.WebProjectRoot + "/", StringComparison.OrdinalIgnoreCase))
        {
            score += 40;
        }

        if (frontend.LayoutMode == FrontendLayoutMode.HostModulePages)
        {
            string hostPrefix = $"{frontend.ModulesRoot}/{frontend.ExemplarModuleName}/";
            if (normalizedPath.StartsWith(hostPrefix, StringComparison.OrdinalIgnoreCase))
            {
                score += 30;
            }
            else if (normalizedPath.StartsWith(frontend.ModulesRoot + "/", StringComparison.OrdinalIgnoreCase))
            {
                score -= 50;
            }
        }

        if (frontend.ForbiddenRoots.Any(root => normalizedPath.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase)))
        {
            score -= 80;
        }

        if (normalizedPath.Contains("/controllers/", StringComparison.OrdinalIgnoreCase)) score += 8;
        if (normalizedPath.Contains("/services/", StringComparison.OrdinalIgnoreCase)) score += 8;
        if (normalizedPath.Contains("/views/", StringComparison.OrdinalIgnoreCase)) score += 8;
        if (normalizedPath.Contains("/proxies/", StringComparison.OrdinalIgnoreCase)) score += 8;

        return score;
    }

    internal static bool IsFrontendModulePath(string path, RepoContract contract)
    {
        string normalized = path.Replace('\\', '/');
        if (normalized.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".vue", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".ts", StringComparison.OrdinalIgnoreCase))
        {
            if (contract.Frontend is not null
                && normalized.StartsWith(contract.Frontend.ModulesRoot + "/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (contract.Frontend is null)
        {
            return normalized.Contains("/controllers/", StringComparison.OrdinalIgnoreCase)
                   || normalized.Contains("/services/", StringComparison.OrdinalIgnoreCase)
                   || normalized.Contains("/views/", StringComparison.OrdinalIgnoreCase)
                   || normalized.Contains("/proxies/", StringComparison.OrdinalIgnoreCase);
        }

        if (!normalized.StartsWith(contract.Frontend.ModulesRoot + "/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return contract.Frontend.RequiredSubfolders.Any(name =>
                   normalized.Contains($"/{name}/", StringComparison.OrdinalIgnoreCase))
               || contract.Frontend.AllowedRootFileNames.Any(name =>
                   normalized.EndsWith("/" + name, StringComparison.OrdinalIgnoreCase));
    }

    private static void AppendCategory(StringBuilder sb, string title, IEnumerable<string> paths, Func<string, bool> predicate, string repoPath)
    {
        var selected = paths.Where(predicate).Take(3).ToList();
        if (selected.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine($"{title}:");
        foreach (var path in selected)
        {
            string relative = Path.GetRelativePath(repoPath, path).Replace('\\', '/');
            sb.AppendLine($"- {relative}");
            sb.AppendLine(ExtractSnippet(path));
        }
    }

    private static string ExtractSnippet(string path)
    {
        try
        {
            int maxLines = 50;
            int maxChars = 1200;
            var lines = File.ReadLines(path).Take(maxLines).ToArray();
            var snippet = string.Join('\n', lines);
            if (snippet.Length > maxChars)
            {
                snippet = snippet[..maxChars];
            }

            return $"  Snippet:\n{Indent(snippet, "    ")}";
        }
        catch
        {
            return "  Snippet: <unavailable>";
        }
    }

    private static string Indent(string text, string prefix) =>
        string.Join('\n', text.Split('\n').Select(line => $"{prefix}{line}"));
}
