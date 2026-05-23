using System.Text.RegularExpressions;

using agents_mcp_rag.Infrastructure;

namespace agents_mcp_rag.Infrastructure.Compliance.DotNet;

internal static class LayerInterfacePairingDiscoverer
{
    private static readonly Regex ClassDeclarationRegex = new(
        @"^\s*public\s+class\s+(?<class>[A-Za-z_][A-Za-z0-9_]*)\s*(?::\s*(?<bases>.+))?\s*\{?",
        RegexOptions.Compiled);

    private static readonly Regex InterfaceDeclarationRegex = new(
        @"\b(?:public\s+)?interface\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Compiled);

    internal static LayerInterfacePairingConvention Discover(string repoPath, LayerConventionProfile profile)
    {
        var pairs = CollectImplementationInterfacePairs(repoPath, profile);
        if (pairs.Count == 0)
        {
            return LayerInterfacePairingConvention.None;
        }

        InterfaceFileNamingPattern namingPattern = DetectNamingPattern(pairs);
        string? preferredDirectory = pairs
            .Select(pair => Path.GetDirectoryName(pair.InterfaceRelativePath)?.Replace('\\', '/'))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .GroupBy(path => path!, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key.Length)
            .Select(group => group.Key)
            .FirstOrDefault();

        var interfaceContents = pairs
            .Select(pair => ReadInterfaceContent(repoPath, pair.InterfaceRelativePath))
            .Where(content => !string.IsNullOrWhiteSpace(content))
            .ToList();

        bool requireInheritanceClause = interfaceContents.Count > 0
                                          && interfaceContents.Count(content => content!.Contains(':')) > 0;
        IReadOnlyList<string> requiredBaseTokens = ExtractRequiredBaseTokens(interfaceContents);

        InterfacePair exemplar = pairs
            .OrderByDescending(pair => pair.InterfaceTypeName.Length)
            .ThenBy(pair => pair.ImplementationRelativePath, StringComparer.OrdinalIgnoreCase)
            .First();

        string? exemplarEntity = LayerConventionProfiles.GetSubjectBaseName(
            Path.GetFileName(exemplar.ImplementationRelativePath),
            profile);

        return new LayerInterfacePairingConvention(
            LayerUsesInterfaces: true,
            NamingPattern: namingPattern,
            PreferredInterfaceDirectory: preferredDirectory,
            RequireInheritanceClause: requireInheritanceClause,
            RequiredBaseTokens: requiredBaseTokens,
            ExemplarEntityName: exemplarEntity,
            ExemplarInterfaceTypeName: exemplar.InterfaceTypeName);
    }

    internal static string? FindInterfaceFile(
        string repoPath,
        LayerInterfacePairingConvention convention,
        LayerConventionProfile profile,
        string subjectBase)
    {
        string interfaceTypeName = convention.ResolveInterfaceTypeName(subjectBase, profile);
        if (string.IsNullOrWhiteSpace(interfaceTypeName))
        {
            return null;
        }

        string expectedFileName = convention.ResolveInterfaceFileName(subjectBase, profile);
        string? byFileName = FindInterfaceFileByName(repoPath, expectedFileName, convention, profile);
        if (!string.IsNullOrWhiteSpace(byFileName))
        {
            return byFileName;
        }

        return FindInterfaceFileByTypeName(repoPath, interfaceTypeName, convention, profile);
    }

    private sealed record InterfacePair(
        string ImplementationRelativePath,
        string InterfaceRelativePath,
        string InterfaceTypeName);

    private static List<InterfacePair> CollectImplementationInterfacePairs(
        string repoPath,
        LayerConventionProfile profile)
    {
        var pairs = new List<InterfacePair>();
        foreach (string implementationPath in Directory.EnumerateFiles(repoPath, $"*{profile.FileSuffix}", SearchOption.AllDirectories))
        {
            if (implementationPath.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                || implementationPath.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string fileName = Path.GetFileName(implementationPath);
            if (!LayerConventionProfiles.MatchesImplementationFile(fileName, profile))
            {
                continue;
            }

            string? interfaceTypeName = TryExtractRoleInterfaceTypeName(implementationPath, profile);
            if (string.IsNullOrWhiteSpace(interfaceTypeName))
            {
                continue;
            }

            string? interfaceRelativePath = FindInterfaceRelativePath(repoPath, interfaceTypeName);
            if (string.IsNullOrWhiteSpace(interfaceRelativePath))
            {
                continue;
            }

            pairs.Add(new InterfacePair(
                Path.GetRelativePath(repoPath, implementationPath).Replace('\\', '/'),
                interfaceRelativePath,
                interfaceTypeName));
        }

        return pairs;
    }

    private static string? TryExtractRoleInterfaceTypeName(string implementationPath, LayerConventionProfile profile)
    {
        string? classLine = File.ReadLines(implementationPath)
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith("public class ", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(classLine))
        {
            return null;
        }

        Match match = ClassDeclarationRegex.Match(classLine);
        if (!match.Success || !match.Groups["bases"].Success)
        {
            return null;
        }

        string className = Path.GetFileNameWithoutExtension(implementationPath);
        string expectedStem = className.EndsWith(profile.RoleName, StringComparison.OrdinalIgnoreCase)
            ? className
            : $"{className}{profile.RoleName}";

        foreach (string inheritedType in SplitInheritedTypes(match.Groups["bases"].Value))
        {
            string normalized = NormalizeGenericToken(inheritedType);
            if (normalized.Contains(profile.RoleName, StringComparison.Ordinal)
                || normalized.Equals($"I{expectedStem}", StringComparison.Ordinal)
                || normalized.Equals($"{expectedStem}Interface", StringComparison.Ordinal))
            {
                return inheritedType.Trim();
            }
        }

        return null;
    }

    private static IEnumerable<string> SplitInheritedTypes(string inheritanceClause)
    {
        foreach (string token in inheritanceClause.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = token.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                yield return trimmed;
            }
        }
    }

    private static string? FindInterfaceRelativePath(string repoPath, string interfaceTypeName)
    {
        foreach (string path in Directory.EnumerateFiles(repoPath, "*.cs", SearchOption.AllDirectories))
        {
            if (path.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                || path.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string content = File.ReadAllText(path);
            if (InterfaceDeclaresType(content, interfaceTypeName))
            {
                return Path.GetRelativePath(repoPath, path).Replace('\\', '/');
            }
        }

        return null;
    }

    private static bool InterfaceDeclaresType(string content, string interfaceTypeName)
    {
        string normalizedTypeName = NormalizeGenericToken(interfaceTypeName);
        foreach (Match match in InterfaceDeclarationRegex.Matches(content))
        {
            if (NormalizeGenericToken(match.Groups["name"].Value)
                .Equals(normalizedTypeName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static InterfaceFileNamingPattern DetectNamingPattern(IReadOnlyList<InterfacePair> pairs)
    {
        if (pairs.Count == 0)
        {
            return InterfaceFileNamingPattern.None;
        }

        int prefixedI = 0;
        int suffixInterface = 0;
        int sameStemDifferentDirectory = 0;

        foreach (InterfacePair pair in pairs)
        {
            string implStem = Path.GetFileNameWithoutExtension(pair.ImplementationRelativePath);
            string interfaceStem = Path.GetFileNameWithoutExtension(pair.InterfaceRelativePath);
            if (interfaceStem.Equals($"I{implStem}", StringComparison.Ordinal))
            {
                prefixedI++;
                continue;
            }

            if (interfaceStem.Equals($"{implStem}Interface", StringComparison.Ordinal))
            {
                suffixInterface++;
                continue;
            }

            if (interfaceStem.Equals(implStem, StringComparison.Ordinal)
                && !string.Equals(
                    Path.GetDirectoryName(pair.ImplementationRelativePath),
                    Path.GetDirectoryName(pair.InterfaceRelativePath),
                    StringComparison.OrdinalIgnoreCase))
            {
                sameStemDifferentDirectory++;
            }
        }

        int threshold = Math.Max(2, (int)Math.Ceiling(pairs.Count * 0.6));
        if (prefixedI >= threshold)
        {
            return InterfaceFileNamingPattern.PrefixedI;
        }

        if (suffixInterface >= threshold)
        {
            return InterfaceFileNamingPattern.SuffixInterface;
        }

        if (sameStemDifferentDirectory >= threshold)
        {
            return InterfaceFileNamingPattern.SameStemDifferentDirectory;
        }

        return InterfaceFileNamingPattern.None;
    }

    private static IReadOnlyList<string> ExtractRequiredBaseTokens(IReadOnlyList<string?> interfaceContents)
    {
        var baseTokenCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        int withAnyBaseClause = 0;
        foreach (string? content in interfaceContents)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            string? declaration = content
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .Select(line => line.Trim())
                .FirstOrDefault(line => line.Contains(" interface ", StringComparison.Ordinal));
            if (string.IsNullOrWhiteSpace(declaration) || !declaration.Contains(':'))
            {
                continue;
            }

            withAnyBaseClause++;
            string inheritance = declaration.Split(':', 2)[1];
            foreach (string token in inheritance.Split(',', StringSplitOptions.RemoveEmptyEntries)
                         .Select(x => NormalizeGenericToken(x.Trim())))
            {
                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                baseTokenCounts[token] = baseTokenCounts.TryGetValue(token, out int count) ? count + 1 : 1;
            }
        }

        if (withAnyBaseClause == 0)
        {
            return Array.Empty<string>();
        }

        int threshold = Math.Max(2, (int)Math.Ceiling(withAnyBaseClause * 0.6));
        return baseTokenCounts
            .Where(kvp => kvp.Value >= threshold)
            .Select(kvp => kvp.Key)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();
    }

    private static string? ReadInterfaceContent(string repoPath, string interfaceRelativePath)
    {
        string absolute = Path.Combine(repoPath, interfaceRelativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(absolute) ? File.ReadAllText(absolute) : null;
    }

    private static string? FindInterfaceFileByName(
        string repoPath,
        string interfaceFileName,
        LayerInterfacePairingConvention convention,
        LayerConventionProfile profile)
    {
        IEnumerable<string> candidates = Directory
            .EnumerateFiles(repoPath, interfaceFileName, SearchOption.AllDirectories)
            .Where(path => !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                        && !path.Contains("/bin/", StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(convention.PreferredInterfaceDirectory))
        {
            string? preferred = candidates.FirstOrDefault(path =>
                Path.GetRelativePath(repoPath, path).Replace('\\', '/')
                    .StartsWith(convention.PreferredInterfaceDirectory + "/", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(preferred))
            {
                return preferred;
            }
        }

        return candidates.FirstOrDefault(path =>
            path.Contains("Interfaces", StringComparison.OrdinalIgnoreCase)
            || path.Contains(profile.RoleName, StringComparison.OrdinalIgnoreCase))
               ?? candidates.FirstOrDefault();
    }

    private static string? FindInterfaceFileByTypeName(
        string repoPath,
        string interfaceTypeName,
        LayerInterfacePairingConvention convention,
        LayerConventionProfile profile)
    {
        string? relativePath = FindInterfaceRelativePath(repoPath, interfaceTypeName);
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        string absolute = Path.Combine(repoPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(absolute) ? absolute : null;
    }

    private static string NormalizeGenericToken(string value) =>
        Regex.Replace(value, @"<\s*[A-Za-z_][A-Za-z0-9_]*\s*>", "<T>");
}
