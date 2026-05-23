using System.Text;
using System.Text.RegularExpressions;

namespace agents_mcp_rag.Infrastructure.CodeApply.DotNet;

/// <summary>
/// Ensures projection/consumer artifacts only reference members declared on a paired definition type.
/// Pairing is inferred from filenames ({Definition}{Suffix}.cs ↔ {Definition}.cs) — no framework-specific markers.
/// </summary>
internal static class TypeMemberConsistencyGuard
{
    private static readonly Regex PublicMemberRegex = new(
        @"public\s+[\w<>\[\],\s\?]+\s+([A-Za-z_][A-Za-z0-9_]*)\s*\{\s*get",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex MemberAccessRegex = new(
        @"\b(?<recv>[a-z][A-Za-z0-9_]*)\.(?<member>[A-Za-z_][A-Za-z0-9_]*)\b",
        RegexOptions.Compiled);

    private static readonly Regex SelectProjectionStartRegex = new(
        @"select\s+new\s*\{",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HashSet<string> IgnoredMemberNames = new(StringComparer.Ordinal)
    {
        "select", "new", "from", "get", "set", "where", "orderby", "group", "join"
    };

    internal static string? BuildRagContext(string repoPath, string? taskSubjectHint)
    {
        DefinitionConsumerPair? exemplar = FindBestExemplarPair(repoPath, taskSubjectHint);
        if (exemplar is null)
        {
            return """
                Definition types and their projection consumers must stay in sync:
                - Declare all members on the definition class first.
                - Consumer projections may only reference members that exist on that type.
                - Mirror the closest existing definition+consumer pair from this repository.
                """;
        }

        var members = ExtractDeclaredMembers(exemplar.DefinitionContent);
        var sb = new StringBuilder();
        sb.AppendLine("Definition + consumer pairing rules (mirror exemplar pair in this repo):");
        sb.AppendLine("- Define the type's members on the definition class first.");
        sb.AppendLine("- Consumer projections may ONLY use members declared on that definition type.");
        sb.AppendLine($"- Exemplar pair: {exemplar.DefinitionRelativePath} ↔ {exemplar.ConsumerRelativePath}");
        sb.AppendLine($"- Valid member names (from exemplar): {string.Join(", ", members.OrderBy(p => p, StringComparer.Ordinal))}");
        sb.AppendLine();
        sb.AppendLine("Exemplar definition:");
        sb.AppendLine(Truncate(exemplar.DefinitionContent, 1200));
        sb.AppendLine();
        sb.AppendLine("Exemplar consumer:");
        sb.AppendLine(Truncate(exemplar.ConsumerContent, 1200));
        return sb.ToString();
    }

    internal static bool TryValidateConsumerContent(
        string repoPath,
        string consumerRelativePath,
        string consumerContent,
        IReadOnlyDictionary<string, string> proposedDefinitions,
        out string reason)
    {
        reason = string.Empty;
        if (!TryResolveDefinitionType(repoPath, consumerRelativePath, proposedDefinitions, out string definitionTypeName))
        {
            return true;
        }

        string? definitionContent = ResolveDefinitionContent(repoPath, definitionTypeName, proposedDefinitions);
        if (string.IsNullOrWhiteSpace(definitionContent))
        {
            return true;
        }

        return TryValidatePair(definitionTypeName, definitionContent, consumerContent, out reason);
    }

    internal static Dictionary<string, string> BuildProposedTypeDefinitions(IReadOnlyList<GeneratedFile> proposedFiles)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in proposedFiles)
        {
            if (IsDefinitionPath(file.RelativePath))
            {
                AddDeclaredTypesFromContent(file.Content, map);
                continue;
            }

            if (!TryExtractTypeNameFromFileName(file.RelativePath, out string typeNameFromFile))
            {
                continue;
            }

            if (map.ContainsKey(typeNameFromFile))
            {
                continue;
            }

            if (ExtractDeclaredMembers(file.Content).Count > 0)
            {
                map[typeNameFromFile] = file.Content;
            }
        }

        return map;
    }

    internal static bool TryValidatePair(
        string definitionTypeName,
        string definitionContent,
        string consumerContent,
        out string reason)
    {
        reason = string.Empty;
        var declared = ExtractDeclaredMembers(definitionContent);
        if (declared.Count == 0)
        {
            return true;
        }

        var referenced = ExtractProjectedMemberReferences(consumerContent);
        var missing = referenced.Where(member => !declared.Contains(member)).Distinct(StringComparer.Ordinal).ToList();
        if (missing.Count == 0)
        {
            return true;
        }

        reason =
            $"Consumer projection references member(s) not declared on {definitionTypeName}: {string.Join(", ", missing)}. "
            + $"Declared members: {string.Join(", ", declared.OrderBy(p => p, StringComparer.Ordinal))}. "
            + "Add members to the definition type or remove them from the consumer projection.";
        return false;
    }

    internal static bool TryResolveDefinitionType(
        string repoPath,
        string consumerRelativePath,
        IReadOnlyDictionary<string, string>? proposedDefinitions,
        out string definitionTypeName)
    {
        definitionTypeName = string.Empty;
        if (!IsProjectionConsumerRelativePath(repoPath, consumerRelativePath, proposedDefinitions))
        {
            return false;
        }

        return TryResolveDefinitionTypeCore(repoPath, consumerRelativePath, proposedDefinitions, out definitionTypeName);
    }

    internal static bool IsConsumerRelativePath(
        string repoPath,
        string relativePath,
        IReadOnlyDictionary<string, string>? proposedDefinitions = null) =>
        IsProjectionConsumerRelativePath(repoPath, relativePath, proposedDefinitions);

    private static bool IsProjectionConsumerRelativePath(
        string repoPath,
        string relativePath,
        IReadOnlyDictionary<string, string>? proposedDefinitions)
    {
        if (IsLayerImplementationFile(repoPath, relativePath)
            || IsTestArtifactPath(relativePath))
        {
            return false;
        }

        return TryResolveDefinitionTypeCore(repoPath, relativePath, proposedDefinitions, out string definitionType)
               && DefinitionExistsInDefinitionPath(repoPath, definitionType, proposedDefinitions);
    }

    private static bool TryResolveDefinitionTypeCore(
        string repoPath,
        string consumerRelativePath,
        IReadOnlyDictionary<string, string>? proposedDefinitions,
        out string definitionTypeName)
    {
        definitionTypeName = string.Empty;
        string stem = Path.GetFileNameWithoutExtension(consumerRelativePath);
        if (stem.Length < 4)
        {
            return false;
        }

        var candidates = new List<string>();
        for (int splitAt = 2; splitAt < stem.Length; splitAt++)
        {
            string candidateType = stem[..splitAt];
            string suffix = stem[splitAt..];
            if (!IsValidConsumerSuffix(suffix) || !IsValidTypeName(candidateType))
            {
                continue;
            }

            if (DefinitionExists(repoPath, candidateType, proposedDefinitions))
            {
                candidates.Add(candidateType);
            }
        }

        if (candidates.Count == 0)
        {
            return false;
        }

        definitionTypeName = candidates.OrderByDescending(name => name.Length).First();
        return true;
    }

    internal static IReadOnlyList<string> DiscoverConsumerSuffixes(string repoPath)
    {
        var suffixes = new HashSet<string>(StringComparer.Ordinal);
        foreach (string consumerPath in EnumerateConsumerPaths(repoPath))
        {
            string relative = Path.GetRelativePath(repoPath, consumerPath).Replace('\\', '/');
            if (!TryResolveDefinitionType(repoPath, relative, proposedDefinitions: null, out string definitionType))
            {
                continue;
            }

            string stem = Path.GetFileNameWithoutExtension(relative);
            if (stem.Length > definitionType.Length)
            {
                suffixes.Add(stem[definitionType.Length..]);
            }
        }

        return suffixes.OrderBy(s => s, StringComparer.Ordinal).ToList();
    }

    private static IEnumerable<string> EnumerateConsumerPaths(string repoPath)
    {
        foreach (string path in Directory.EnumerateFiles(repoPath, "*.cs", SearchOption.AllDirectories))
        {
            if (path.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                || path.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string relative = Path.GetRelativePath(repoPath, path).Replace('\\', '/');
            if (IsDefinitionPath(relative))
            {
                continue;
            }

            if (IsProjectionConsumerRelativePath(repoPath, relative, proposedDefinitions: null))
            {
                yield return path;
            }
        }
    }

    private static bool IsLayerImplementationFile(string repoPath, string relativePath)
    {
        string fileName = Path.GetFileName(relativePath);
        var profiles = LayerConventionProfileBuilder.Build(repoPath);
        return profiles.GetActiveProfiles().Any(profile =>
            LayerConventionProfiles.MatchesImplementationFile(fileName, profile));
    }

    private static bool IsTestArtifactPath(string relativePath)
    {
        string fileName = Path.GetFileName(relativePath);
        return fileName.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase)
               || relativePath.Contains("/UnitTest/", StringComparison.OrdinalIgnoreCase)
               || relativePath.Contains("\\UnitTest\\", StringComparison.OrdinalIgnoreCase);
    }

    private static bool DefinitionExistsInDefinitionPath(
        string repoPath,
        string definitionTypeName,
        IReadOnlyDictionary<string, string>? proposedDefinitions)
    {
        if (proposedDefinitions?.ContainsKey(definitionTypeName) == true)
        {
            return true;
        }

        return Directory
            .EnumerateFiles(repoPath, $"{definitionTypeName}.cs", SearchOption.AllDirectories)
            .Any(path => IsDefinitionPath(path)
                      && !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                      && !path.Contains("/bin/", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsValidConsumerSuffix(string suffix) =>
        suffix.Length >= 2 && char.IsUpper(suffix[0]);

    private static bool IsValidTypeName(string typeName) =>
        typeName.Length >= 2 && char.IsUpper(typeName[0]);

    private static bool TryExtractTypeNameFromFileName(string relativePath, out string typeName)
    {
        typeName = Path.GetFileNameWithoutExtension(relativePath);
        return IsValidTypeName(typeName);
    }

    private static void AddDeclaredTypesFromContent(string content, Dictionary<string, string> map)
    {
        foreach (Match match in Regex.Matches(content, @"\bclass\s+([A-Za-z_][A-Za-z0-9_]*)\b"))
        {
            map[match.Groups[1].Value] = content;
        }
    }

    private static bool DefinitionExists(
        string repoPath,
        string definitionTypeName,
        IReadOnlyDictionary<string, string>? proposedDefinitions)
    {
        if (proposedDefinitions?.ContainsKey(definitionTypeName) == true)
        {
            return true;
        }

        return Directory
            .EnumerateFiles(repoPath, $"{definitionTypeName}.cs", SearchOption.AllDirectories)
            .Any(path => !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                      && !path.Contains("/bin/", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsDefinitionPath(string relativePath) =>
        relativePath.Contains("/Entities/", StringComparison.OrdinalIgnoreCase)
        || relativePath.Contains("\\Entities\\", StringComparison.OrdinalIgnoreCase)
        || relativePath.Contains("/Models/", StringComparison.OrdinalIgnoreCase)
        || relativePath.Contains("\\Models\\", StringComparison.OrdinalIgnoreCase)
        || relativePath.Contains("/Domain/", StringComparison.OrdinalIgnoreCase)
        || relativePath.Contains("\\Domain\\", StringComparison.OrdinalIgnoreCase);

    private static string? ResolveDefinitionContent(
        string repoPath,
        string definitionTypeName,
        IReadOnlyDictionary<string, string> proposedDefinitions)
    {
        if (proposedDefinitions.TryGetValue(definitionTypeName, out string? proposed))
        {
            return proposed;
        }

        return Directory
            .EnumerateFiles(repoPath, $"{definitionTypeName}.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                        && !path.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => IsDefinitionPath(path) ? 0 : 1)
            .Select(File.ReadAllText)
            .FirstOrDefault();
    }

    private static HashSet<string> ExtractDeclaredMembers(string definitionContent)
    {
        var members = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in PublicMemberRegex.Matches(definitionContent))
        {
            members.Add(match.Groups[1].Value);
        }

        return members;
    }

    private static HashSet<string> ExtractProjectedMemberReferences(string consumerContent)
    {
        var members = new HashSet<string>(StringComparer.Ordinal);
        if (TryExtractMembersFromSelectProjections(consumerContent, members))
        {
            return members;
        }

        foreach (Match match in MemberAccessRegex.Matches(consumerContent))
        {
            if (IsFrameworkReceiver(match.Groups["recv"].Value))
            {
                continue;
            }

            string member = match.Groups["member"].Value;
            if (IgnoredMemberNames.Contains(member))
            {
                continue;
            }

            members.Add(member);
        }

        return members;
    }

    /// <summary>Index/DTO projections: only validate entity members inside "select new { ... }".</summary>
    private static bool TryExtractMembersFromSelectProjections(string content, HashSet<string> members)
    {
        bool foundProjection = false;
        foreach (Match selectMatch in SelectProjectionStartRegex.Matches(content))
        {
            int braceIndex = selectMatch.Index + selectMatch.Length - 1;
            if (braceIndex < 0 || braceIndex >= content.Length || content[braceIndex] != '{')
            {
                continue;
            }

            if (!TryReadBracedBlock(content, braceIndex, out string block))
            {
                continue;
            }

            foundProjection = true;
            foreach (Match match in MemberAccessRegex.Matches(block))
            {
                if (IsFrameworkReceiver(match.Groups["recv"].Value))
                {
                    continue;
                }

                string member = match.Groups["member"].Value;
                if (!IgnoredMemberNames.Contains(member))
                {
                    members.Add(member);
                }
            }
        }

        return foundProjection;
    }

    private static bool TryReadBracedBlock(string content, int openBraceIndex, out string block)
    {
        block = string.Empty;
        int depth = 0;
        for (int i = openBraceIndex; i < content.Length; i++)
        {
            char c = content[i];
            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    block = content[openBraceIndex..(i + 1)];
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsFrameworkReceiver(string receiver) =>
        receiver.Equals("this", StringComparison.Ordinal)
        || receiver.Equals("base", StringComparison.Ordinal);

    private static DefinitionConsumerPair? FindBestExemplarPair(string repoPath, string? taskSubjectHint)
    {
        var signals = ExtractNameTokens(taskSubjectHint);
        var candidates = new List<(DefinitionConsumerPair Pair, int Score)>();

        foreach (string consumerPath in EnumerateConsumerPaths(repoPath))
        {
            string consumerRelative = Path.GetRelativePath(repoPath, consumerPath).Replace('\\', '/');
            if (!TryResolveDefinitionTypeCore(repoPath, consumerRelative, proposedDefinitions: null, out string definitionTypeName))
            {
                continue;
            }

            string? definitionPath = Directory
                .EnumerateFiles(repoPath, $"{definitionTypeName}.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                             && !path.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => IsDefinitionPath(path) ? 0 : 1)
                .ThenBy(path => path.Length)
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(definitionPath))
            {
                continue;
            }

            int score = signals.Count(signal =>
                definitionTypeName.Contains(signal, StringComparison.OrdinalIgnoreCase)
                || consumerRelative.Contains(signal, StringComparison.OrdinalIgnoreCase));
            score += IsDefinitionPath(definitionPath) ? 2 : 0;

            candidates.Add((new DefinitionConsumerPair(
                Path.GetRelativePath(repoPath, definitionPath).Replace('\\', '/'),
                File.ReadAllText(definitionPath),
                consumerRelative,
                File.ReadAllText(consumerPath)), score));
        }

        return candidates.OrderByDescending(c => c.Score).ThenBy(c => c.Pair.ConsumerRelativePath.Length).FirstOrDefault().Pair;
    }

    private static List<string> ExtractNameTokens(string? text)
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

    private static string Truncate(string content, int maxChars) =>
        content.Length > maxChars ? content[..maxChars] + "\n// [truncated]" : content;

    private sealed record DefinitionConsumerPair(
        string DefinitionRelativePath,
        string DefinitionContent,
        string ConsumerRelativePath,
        string ConsumerContent);
}
