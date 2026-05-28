using System.Text;

namespace workflowX.Infrastructure.Rag.Frontend;

/// <summary>
/// Frontend (JS/TS/HTML) RAG context — only invoked when <see cref="RepoStack.Frontend"/>.
/// </summary>
internal static class FrontendRagContextSupport
{
    internal static void AppendImplementationContext(
        StringBuilder sb,
        IReadOnlyList<string> candidatePaths,
        RepoContract contract,
        string repoPath)
    {
        AppendCategory(sb, "Frontend UI modules", candidatePaths, path => IsFrontendModulePath(path, contract), repoPath);
        AppendCategory(sb, "Frontend tests", candidatePaths, path =>
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
        var selected = paths
            .Where(predicate)
            .Select(path => (Absolute: path, Relative: Path.GetRelativePath(repoPath, path).Replace('\\', '/')))
            .OrderBy(x => x.Relative, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .Select(x => x.Absolute)
            .ToList();
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
