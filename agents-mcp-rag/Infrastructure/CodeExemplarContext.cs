using System.Text;
using System.Text.RegularExpressions;

namespace agents_mcp_rag.Infrastructure;

/// <summary>
/// Discovers closest existing source files in the target repo to use as implementation exemplars (layer-agnostic).
/// </summary>
static class CodeExemplarContext
{
    private const int MaxExemplarChars = 3200;

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

    internal static string BuildForCompilationFix(string repoPath, IReadOnlyList<string> allowedFiles)
    {
        if (allowedFiles.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine("Implementation exemplars for files being fixed:");

        var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in allowedFiles.Where(p => p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)).Take(12))
        {
            string? exemplar = FindClosestExemplar(repoPath, target);
            if (string.IsNullOrWhiteSpace(exemplar) || !added.Add(exemplar))
            {
                continue;
            }

            AppendFileExcerpt(sb, repoPath, exemplar, $"similar to {target}");
        }

        return sb.Length > 80 ? sb.ToString() : string.Empty;
    }

    internal static string? FindClosestExemplar(string repoPath, string targetRelativePath)
    {
        string targetFileName = Path.GetFileName(targetRelativePath);
        string targetDir = Path.GetDirectoryName(targetRelativePath.Replace('\\', '/')) ?? string.Empty;
        string targetStem = GetFileStem(targetFileName);

        var candidates = Directory
            .EnumerateFiles(repoPath, "*" + Path.GetExtension(targetFileName), SearchOption.AllDirectories)
            .Where(path => !IsExcludedPath(path))
            .Select(path => Path.GetRelativePath(repoPath, path).Replace('\\', '/'))
            .Where(relative => !relative.Equals(targetRelativePath, StringComparison.OrdinalIgnoreCase))
            .Select(relative => new
            {
                Relative = relative,
                Score = ScoreExemplar(targetRelativePath, targetFileName, targetDir, targetStem, relative)
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Relative.Length)
            .ToList();

        return candidates.FirstOrDefault()?.Relative;
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

    private static int ScoreExemplar(
        string targetRelative,
        string targetFileName,
        string targetDir,
        string targetStem,
        string candidateRelative)
    {
        string candidateFileName = Path.GetFileName(candidateRelative);
        if (candidateFileName.Equals(targetFileName, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        int score = 0;
        string candidateDir = Path.GetDirectoryName(candidateRelative.Replace('\\', '/')) ?? string.Empty;
        if (candidateDir.Equals(targetDir, StringComparison.OrdinalIgnoreCase))
        {
            score += 8;
        }
        else if (Path.GetFileName(candidateDir).Equals(Path.GetFileName(targetDir), StringComparison.OrdinalIgnoreCase))
        {
            score += 4;
        }

        string candidateStem = GetFileStem(candidateFileName);
        if (ShareSameLayerSuffix(targetStem, candidateStem))
        {
            score += 6;
        }

        if (candidateFileName.StartsWith('I') == targetFileName.StartsWith('I'))
        {
            score += 1;
        }

        return score;
    }

    private static bool ShareSameLayerSuffix(string targetStem, string candidateStem)
    {
        foreach (string suffix in new[] { "Repository", "Service", "Controller", "Handler", "Provider", "Manager" })
        {
            if (targetStem.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                && candidateStem.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return targetStem.Length > 3 && candidateStem.Length > 3;
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

    private static string GetFileStem(string fileName)
    {
        return Path.GetFileNameWithoutExtension(fileName);
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
