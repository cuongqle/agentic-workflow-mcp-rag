using System.Text.RegularExpressions;

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

        foreach (var file in files)
        {
            var analyzed = AnalyzeClassFile(file, roleName);
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
            RequireMatchingRoleInterface: (double)withMatchingInterface / sampleCount >= strongThreshold,
            RequireBaseConstructorCall: (double)withBaseCtorCall / sampleCount >= strongThreshold,
            RequiredInheritedTypeTokens: requiredInheritedTypes,
            RequiredConstructorParamTypes: requiredCtorParamTypes);
    }

    private static AnalyzedClassFile? AnalyzeClassFile(string filePath, string roleName)
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

        string entityName = className.EndsWith(roleName, StringComparison.OrdinalIgnoreCase)
            ? className[..^roleName.Length]
            : className;
        string expectedRoleInterface = $"I{entityName}{roleName}";
        bool hasMatchingRoleInterface = inheritedTypes.Any(type =>
            type.Equals(expectedRoleInterface, StringComparison.Ordinal));
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

    private sealed record AnalyzedClassFile(
        bool HasInheritanceClause,
        List<string> InheritedTypes,
        bool HasMatchingRoleInterface,
        bool HasBaseCtorCall,
        List<string> ConstructorParamTypes);
}

sealed class LayerConventionProfiles
{
    public static LayerConventionProfiles Empty { get; } = new(Array.Empty<LayerConventionProfile>());

    private readonly IReadOnlyList<LayerConventionProfile> _profiles;

    public LayerConventionProfiles(IReadOnlyList<LayerConventionProfile> profiles)
    {
        _profiles = profiles;
    }

    public LayerConventionProfile? ResolveByPath(string relativePath)
    {
        string fileName = Path.GetFileName(relativePath);
        foreach (var profile in GetActiveProfiles())
        {
            if (MatchesImplementationFile(fileName, profile))
            {
                return profile;
            }
        }

        return null;
    }

    public IEnumerable<LayerConventionProfile> GetActiveProfiles() => _profiles;

    internal static bool MatchesImplementationFile(string fileName, LayerConventionProfile profile)
    {
        if (!fileName.EndsWith(profile.FileSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (fileName.StartsWith("I", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string roleBaseFile = profile.RoleName + Path.GetExtension(profile.FileSuffix);
        return !fileName.Equals(roleBaseFile, StringComparison.OrdinalIgnoreCase);
    }

    internal static string? GetSubjectBaseName(string fileName, LayerConventionProfile profile)
    {
        if (!MatchesImplementationFile(fileName, profile))
        {
            return null;
        }

        string stem = Path.GetFileNameWithoutExtension(fileName);
        if (stem.EndsWith(profile.RoleName, StringComparison.OrdinalIgnoreCase)
            && stem.Length > profile.RoleName.Length)
        {
            return stem[..^profile.RoleName.Length];
        }

        return stem;
    }

    internal static string BuildExpectedInterfaceFileName(string subjectBase, LayerConventionProfile profile) =>
        $"I{subjectBase}{profile.RoleName}.cs";

    internal static string BuildExpectedImplementationFileName(string subjectBase, LayerConventionProfile profile) =>
        $"{subjectBase}{profile.RoleName}.cs";

    internal static IEnumerable<string> ResolveRequiredConstructorParamTypes(
        string repoPath,
        string subjectBase,
        LayerConventionProfile profile)
    {
        var required = new HashSet<string>(profile.RequiredConstructorParamTypes, StringComparer.Ordinal);
        if (profile.RequireMatchingRoleInterface)
        {
            required.Add($"I{subjectBase}{profile.RoleName}");
        }

        string exemplarFileName = $"{subjectBase}{profile.RoleName}.cs";
        string? exemplarPath = Directory
            .EnumerateFiles(repoPath, exemplarFileName, SearchOption.AllDirectories)
            .FirstOrDefault(path => !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                                 && !path.Contains("/bin/", StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(exemplarPath))
        {
            string exemplarContent = File.ReadAllText(exemplarPath);
            foreach (string paramType in profile.RequiredConstructorParamTypes)
            {
                if (exemplarContent.Contains(paramType, StringComparison.Ordinal))
                {
                    required.Add(paramType);
                }
            }
        }

        return required;
    }

    private static readonly Regex ConstructorPatternRegex = new(
        @"public\s+(?<class>[A-Za-z_][A-Za-z0-9_]*)\s*\((?<params>[^)]*)\)\s*(?::\s*base\s*\((?<baseArgs>[^)]*)\))?\s*\{",
        RegexOptions.Multiline | RegexOptions.Compiled);

    public bool ValidateAgainstExemplar(
        string repoPath,
        string relativePath,
        string content,
        string className,
        LayerConventionProfile profile,
        out string reason)
    {
        reason = string.Empty;
        if (!profile.RequireBaseConstructorCall)
        {
            return true;
        }

        LayerExemplarConstructor? exemplar = FindExemplarConstructor(repoPath, relativePath, profile);
        if (exemplar is null || MatchesExemplarConstructor(content, className, exemplar))
        {
            return true;
        }

        reason =
            $"Must match layer exemplar {exemplar.SourceRelativePath} "
            + $"(public {className}({exemplar.ParameterList}) : base({exemplar.BaseArgumentList})).";
        return false;
    }

    private static bool MatchesExemplarConstructor(
        string content,
        string className,
        LayerExemplarConstructor exemplar)
    {
        Match current = ConstructorPatternRegex.Match(content);
        return current.Success
               && current.Groups["class"].Value.Equals(className, StringComparison.Ordinal)
               && current.Groups["baseArgs"].Value.Trim().Equals(exemplar.BaseArgumentList.Trim(), StringComparison.Ordinal);
    }

    private static LayerExemplarConstructor? FindExemplarConstructor(
        string repoPath,
        string relativePath,
        LayerConventionProfile profile)
    {
        string? directory = Path.GetDirectoryName(Path.Combine(repoPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        IEnumerable<string> candidates = !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, $"*{profile.FileSuffix}", SearchOption.TopDirectoryOnly)
            : Directory.EnumerateFiles(repoPath, $"*{profile.FileSuffix}", SearchOption.AllDirectories);

        string selfFullPath = Path.GetFullPath(Path.Combine(repoPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        foreach (string candidate in candidates
                     .Where(path => !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                                 && !path.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(path => path.Length))
        {
            if (Path.GetFullPath(candidate).Equals(selfFullPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string className = Path.GetFileNameWithoutExtension(candidate);
            Match match = ConstructorPatternRegex.Match(File.ReadAllText(candidate));
            if (!match.Success
                || !match.Groups["class"].Value.Equals(className, StringComparison.Ordinal)
                || !match.Groups["baseArgs"].Success)
            {
                continue;
            }

            return new LayerExemplarConstructor(
                Path.GetRelativePath(repoPath, candidate).Replace('\\', '/'),
                match.Groups["params"].Value.Trim(),
                match.Groups["baseArgs"].Value.Trim());
        }

        return null;
    }

    private sealed record LayerExemplarConstructor(
        string SourceRelativePath,
        string ParameterList,
        string BaseArgumentList);
}

sealed record LayerConventionProfile(
    string RoleName,
    string FileSuffix,
    int SampleCount,
    string? CanonicalDirectory,
    bool RequireInheritanceClause,
    bool RequireMatchingRoleInterface,
    bool RequireBaseConstructorCall,
    IReadOnlyList<string> RequiredInheritedTypeTokens,
    IReadOnlyList<string> RequiredConstructorParamTypes);
