using System.Text;
using System.Text.RegularExpressions;

namespace agents_mcp_rag.Infrastructure;

/// <summary>
/// C# codebase helpers: exemplar discovery for RAG/compilation context and lightweight syntax validation.
/// </summary>
static class CodeExemplarContext
{
    private const int MaxExemplarChars = 3200;

    internal static bool TryValidate(string content, out string reason)
    {
        reason = string.Empty;
        if (string.IsNullOrWhiteSpace(content))
        {
            reason = "C# content is empty.";
            return false;
        }

        if (!HasBalancedPairs(content, '(', ')'))
        {
            reason = "Unbalanced parentheses.";
            return false;
        }

        if (!HasBalancedPairs(content, '{', '}'))
        {
            reason = "Unbalanced braces.";
            return false;
        }

        if (!HasBalancedPairs(content, '[', ']'))
        {
            reason = "Unbalanced brackets.";
            return false;
        }

        if (content.Contains(";;", StringComparison.Ordinal))
        {
            reason = "Contains invalid double semicolon (;;).";
            return false;
        }

        foreach (var line in content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (trimmed.EndsWith(",;", StringComparison.Ordinal)
                || trimmed.EndsWith("(;", StringComparison.Ordinal)
                || trimmed.EndsWith("[;", StringComparison.Ordinal))
            {
                reason = $"Suspicious statement terminator in line: {trimmed}";
                return false;
            }
        }

        return true;
    }

    private static bool HasBalancedPairs(string content, char open, char close)
    {
        int depth = 0;
        bool inString = false;
        bool inChar = false;
        bool escape = false;

        for (int i = 0; i < content.Length; i++)
        {
            char c = content[i];
            if (escape)
            {
                escape = false;
                continue;
            }

            if (c == '\\' && (inString || inChar))
            {
                escape = true;
                continue;
            }

            if (c == '"' && !inChar)
            {
                inString = !inString;
                continue;
            }

            if (c == '\'' && !inString)
            {
                inChar = !inChar;
                continue;
            }

            if (inString || inChar)
            {
                continue;
            }

            if (c == open)
            {
                depth++;
            }
            else if (c == close)
            {
                depth--;
                if (depth < 0)
                {
                    return false;
                }
            }
        }

        return depth == 0 && !inString && !inChar;
    }

    internal static void AppendDiscoveredExemplars(StringBuilder sb, string repoPath, string taskPrompt)
    {
        var signals = ExtractNameTokens(taskPrompt);
        var exemplars = FindLayerExemplars(repoPath, signals, maxPerLayer: 2);
        if (exemplars.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine("Closest implementation exemplars from this repository (mirror structure and APIs — do not invent members):");
        foreach (var exemplar in exemplars)
        {
            AppendFileExcerpt(sb, repoPath, exemplar.RelativePath, exemplar.Reason);
        }
    }

    private static IReadOnlyList<(string RelativePath, string Reason)> FindLayerExemplars(
        string repoPath,
        IReadOnlyList<string> signals,
        int maxPerLayer)
    {
        var results = new List<(string, string)>();
        var layerGroups = Directory
            .EnumerateFiles(repoPath, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsExcludedPath(path))
            .Select(path => Path.GetRelativePath(repoPath, path).Replace('\\', '/'))
            .GroupBy(path => InferLayerKey(path), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Key.Length > 0 && g.Count() >= 2);

        foreach (var group in layerGroups)
        {
            var ranked = group
                .Select(path => new { Path = path, Score = signals.Sum(s => path.Contains(s, StringComparison.OrdinalIgnoreCase) ? 2 : 0) })
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Path.Length)
                .Take(maxPerLayer)
                .ToList();

            foreach (var item in ranked)
            {
                results.Add((item.Path, $"layer: {group.Key}"));
            }
        }

        return results;
    }

    private static void AppendFileExcerpt(StringBuilder sb, string repoPath, string relativePath, string reason)
    {
        string absolute = Path.Combine(repoPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(absolute))
        {
            return;
        }

        string content = File.ReadAllText(absolute);
        if (content.Length > MaxExemplarChars)
        {
            content = content[..MaxExemplarChars] + "\n// [exemplar truncated]";
        }

        sb.AppendLine();
        sb.AppendLine($"- {relativePath} ({reason})");
        sb.AppendLine(content);
    }

    private static string InferLayerKey(string relativePath)
    {
        string fileName = Path.GetFileName(relativePath);
        foreach (string suffix in new[] { "RepositoryTests", "Repository", "Service", "Controller", "Handler" })
        {
            if (fileName.Contains(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return suffix;
            }
        }

        string? dir = Path.GetDirectoryName(relativePath.Replace('\\', '/'));
        return dir ?? string.Empty;
    }

    private static List<string> ExtractNameTokens(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new List<string>();
        }

        return Regex.Matches(text, @"\b[A-Z][A-Za-z0-9_]{2,}\b")
            .Select(m => m.Value)
            .Distinct(StringComparer.Ordinal)
            .Take(8)
            .ToList();
    }

    private static bool IsExcludedPath(string absolutePath)
    {
        string normalized = absolutePath.Replace('\\', '/');
        return normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("/node_modules/", StringComparison.OrdinalIgnoreCase);
    }
}
