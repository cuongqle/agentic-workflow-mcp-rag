using System.Text.RegularExpressions;

namespace agents_mcp_rag.Infrastructure.Compliance.DotNet;

static class LayerConventionProfileBuilder
{
    private static readonly Regex RoleSuffixFromFileRegex = new(
        @"^(?<entity>[A-Za-z][A-Za-z0-9_]*)(?<role>[A-Z][a-zA-Z0-9]{2,})\.cs$",
        RegexOptions.Compiled);

    private static readonly HashSet<string> IgnoredRoleSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Tests", "Test", "Index", "Expression", "Designer", "AssemblyInfo"
    };

    public static LayerConventionProfiles Build(string repoPath)
    {
        if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
        {
            return LayerConventionProfiles.Empty;
        }

        var profiles = new List<LayerConventionProfile>();
        foreach (var discovered in DiscoverRoleSuffixes(repoPath))
        {
            LayerConventionProfile? profile = BuildRoleProfile(
                repoPath,
                discovered.RoleName,
                discovered.FileSuffix,
                discovered.SkipBaseFileName);
            if (profile is not null)
            {
                profiles.Add(profile);
            }
        }

        return new LayerConventionProfiles(profiles);
    }

    private static IEnumerable<(string RoleName, string FileSuffix, string? SkipBaseFileName)> DiscoverRoleSuffixes(
        string repoPath)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (string path in Directory.EnumerateFiles(repoPath, "*.cs", SearchOption.AllDirectories))
        {
            if (path.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                || path.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string fileName = Path.GetFileName(path);
            if (fileName.StartsWith("I", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!RoleSuffixFromFileRegex.IsMatch(fileName))
            {
                continue;
            }

            string role = RoleSuffixFromFileRegex.Match(fileName).Groups["role"].Value;
            if (IgnoredRoleSuffixes.Contains(role))
            {
                continue;
            }

            counts.TryGetValue(role, out int count);
            counts[role] = count + 1;
        }

        foreach (var (roleName, count) in counts.Where(kvp => kvp.Value >= 2).OrderByDescending(kvp => kvp.Value))
        {
            string fileSuffix = $"{roleName}.cs";
            string? skipBase = Directory
                .EnumerateFiles(repoPath, fileSuffix, SearchOption.AllDirectories)
                .Any(path => Path.GetFileName(path).Equals(fileSuffix, StringComparison.OrdinalIgnoreCase)
                          && !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                          && !path.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
                ? fileSuffix
                : null;

            yield return (roleName, fileSuffix, skipBase);
        }
    }

    private static LayerConventionProfile? BuildRoleProfile(
        string repoPath,
        string roleName,
        string suffix,
        string? skipBaseFileName)
    {
        var files = Directory.EnumerateFiles(repoPath, $"*{suffix}", SearchOption.AllDirectories)
            .Where(path => !Path.GetFileName(path).StartsWith("I", StringComparison.OrdinalIgnoreCase))
            .Where(path => string.IsNullOrWhiteSpace(skipBaseFileName)
                           || !Path.GetFileName(path).Equals(skipBaseFileName, StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                        && !path.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
            .Take(80)
            .ToList();
        if (files.Count < 2)
        {
            return null;
        }

        int sampleCount = 0;
        int withInheritance = 0;
        int withBaseCtorCall = 0;
        int withMatchingInterface = 0;
        var inheritedTypeCount = new Dictionary<string, int>(StringComparer.Ordinal);
        var constructorParamTypeCount = new Dictionary<string, int>(StringComparer.Ordinal);

        LayerConventionProfile discoveryProfile = CreateDiscoveryProfile(roleName, suffix);
        LayerInterfacePairingConvention interfacePairing =
            LayerInterfacePairingDiscoverer.Discover(repoPath, discoveryProfile);

        foreach (var file in files)
        {
            var analyzed = AnalyzeClassFile(file, discoveryProfile, interfacePairing);
            if (analyzed is null)
            {
                continue;
            }

            sampleCount++;
            if (analyzed.HasInheritanceClause)
            {
                withInheritance++;
            }

            if (analyzed.HasBaseCtorCall)
            {
                withBaseCtorCall++;
            }

            if (analyzed.HasMatchingRoleInterface)
            {
                withMatchingInterface++;
            }

            foreach (var inherited in analyzed.InheritedTypes)
            {
                inheritedTypeCount[inherited] = inheritedTypeCount.TryGetValue(inherited, out int count) ? count + 1 : 1;
            }

            foreach (var ctorType in analyzed.ConstructorParamTypes)
            {
                constructorParamTypeCount[ctorType] =
                    constructorParamTypeCount.TryGetValue(ctorType, out int count) ? count + 1 : 1;
            }
        }

        if (sampleCount < 2)
        {
            return null;
        }

        const double strongThreshold = 0.7;
        var requiredInheritedTypes = inheritedTypeCount
            .Where(kvp => (double)kvp.Value / sampleCount >= strongThreshold)
            .Select(kvp => kvp.Key)
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .OrderBy(type => type, StringComparer.Ordinal)
            .ToList();
        var requiredCtorParamTypes = constructorParamTypeCount
            .Where(kvp => (double)kvp.Value / sampleCount >= strongThreshold)
            .Select(kvp => kvp.Key)
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .OrderBy(type => type, StringComparer.Ordinal)
            .ToList();

        string? canonicalDirectory = files
            .Select(path => Path.GetRelativePath(repoPath, Path.GetDirectoryName(path) ?? string.Empty).Replace('\\', '/'))
            .Where(relative => !string.IsNullOrWhiteSpace(relative))
            .GroupBy(relative => relative, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key.Length)
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()
            ?.Key;

        return new LayerConventionProfile(
            RoleName: roleName,
            FileSuffix: suffix,
            SampleCount: sampleCount,
            CanonicalDirectory: canonicalDirectory,
            RequireInheritanceClause: (double)withInheritance / sampleCount >= strongThreshold,
            RequireMatchingRoleInterface: interfacePairing.LayerUsesInterfaces
                                          && (double)withMatchingInterface / sampleCount >= strongThreshold,
            RequireBaseConstructorCall: (double)withBaseCtorCall / sampleCount >= strongThreshold,
            RequiredInheritedTypeTokens: requiredInheritedTypes,
            RequiredConstructorParamTypes: requiredCtorParamTypes,
            InterfacePairing: interfacePairing);
    }

    private static LayerConventionProfile CreateDiscoveryProfile(string roleName, string suffix) =>
        new(
            RoleName: roleName,
            FileSuffix: suffix,
            SampleCount: 0,
            CanonicalDirectory: null,
            RequireInheritanceClause: false,
            RequireMatchingRoleInterface: false,
            RequireBaseConstructorCall: false,
            RequiredInheritedTypeTokens: Array.Empty<string>(),
            RequiredConstructorParamTypes: Array.Empty<string>(),
            InterfacePairing: LayerInterfacePairingConvention.None);

    private static AnalyzedClassFile? AnalyzeClassFile(
        string filePath,
        LayerConventionProfile profile,
        LayerInterfacePairingConvention interfacePairing)
    {
        string[] lines = File.ReadAllLines(filePath);
        string content = string.Join('\n', lines);
        string? classLine = lines
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith("public class ", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(classLine))
        {
            return null;
        }

        string className = Path.GetFileNameWithoutExtension(filePath);
        var inheritedTypes = new List<string>();
        bool hasInheritanceClause = classLine.Contains(':');
        if (hasInheritanceClause)
        {
            string inheritance = classLine.Split(':', 2)[1];
            inheritedTypes = inheritance
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(type => type.Trim())
                .Where(type => !string.IsNullOrWhiteSpace(type))
                .ToList();
        }

        string? subjectBase = LayerConventionProfiles.GetSubjectBaseName(Path.GetFileName(filePath), profile);
        string entityName = subjectBase
                              ?? (className.EndsWith(profile.RoleName, StringComparison.OrdinalIgnoreCase)
                                  ? className[..^profile.RoleName.Length]
                                  : className);
        string expectedRoleInterface = interfacePairing.ResolveInterfaceTypeName(entityName, profile);
        bool hasMatchingRoleInterface = !string.IsNullOrWhiteSpace(expectedRoleInterface)
                                          && inheritedTypes.Any(type => TypeNamesMatch(type, expectedRoleInterface));
        bool hasBaseCtorCall = content.Contains("base(", StringComparison.Ordinal);

        var ctorParamTypes = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match ctorMatch in Regex.Matches(content, $@"public\s+{Regex.Escape(className)}\s*\(([^)]*)\)"))
        {
            if (ctorMatch.Groups.Count < 2)
            {
                continue;
            }

            string paramBlock = ctorMatch.Groups[1].Value;
            foreach (var param in paramBlock.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                string normalized = Regex.Replace(param.Trim(), @"\s+", " ");
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    continue;
                }

                string[] parts = normalized.Split(' ');
                if (parts.Length >= 2)
                {
                    ctorParamTypes.Add(parts[0].Trim());
                }
            }
        }

        return new AnalyzedClassFile(
            hasInheritanceClause,
            inheritedTypes,
            hasMatchingRoleInterface,
            hasBaseCtorCall,
            ctorParamTypes.ToList());
    }

    private static bool TypeNamesMatch(string actual, string expected) =>
        actual.Equals(expected, StringComparison.Ordinal)
        || StripGenericArity(actual).Equals(StripGenericArity(expected), StringComparison.Ordinal);

    private static string StripGenericArity(string typeName)
    {
        int generic = typeName.IndexOf('<');
        return generic > 0 ? typeName[..generic] : typeName;
    }

    private sealed record AnalyzedClassFile(
        bool HasInheritanceClause,
        List<string> InheritedTypes,
        bool HasMatchingRoleInterface,
        bool HasBaseCtorCall,
        List<string> ConstructorParamTypes);
}
