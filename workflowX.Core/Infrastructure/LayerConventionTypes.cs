using System.Text.RegularExpressions;

namespace workflowX.Infrastructure;

public enum InterfaceFileNamingPattern
{
    None,
    PrefixedI,
    SuffixInterface,
    SameStemDifferentDirectory
}

public sealed record LayerInterfacePairingConvention(
    bool LayerUsesInterfaces,
    InterfaceFileNamingPattern NamingPattern,
    string? PreferredInterfaceDirectory,
    bool RequireInheritanceClause,
    IReadOnlyList<string> RequiredBaseTokens,
    string? ExemplarEntityName,
    string? ExemplarInterfaceTypeName)
{
    public static LayerInterfacePairingConvention None { get; } = new(
        LayerUsesInterfaces: false,
        NamingPattern: InterfaceFileNamingPattern.None,
        PreferredInterfaceDirectory: null,
        RequireInheritanceClause: false,
        RequiredBaseTokens: Array.Empty<string>(),
        ExemplarEntityName: null,
        ExemplarInterfaceTypeName: null);

    public string ResolveInterfaceTypeName(string subjectBase, LayerConventionProfile profile)
    {
        if (!LayerUsesInterfaces || string.IsNullOrWhiteSpace(subjectBase))
        {
            return string.Empty;
        }

        string implementationStem = $"{subjectBase}{profile.RoleName}";
        return NamingPattern switch
        {
            InterfaceFileNamingPattern.PrefixedI => $"I{implementationStem}",
            InterfaceFileNamingPattern.SuffixInterface => $"{implementationStem}Interface",
            InterfaceFileNamingPattern.SameStemDifferentDirectory => implementationStem,
            _ => ResolveInterfaceTypeNameFromExemplar(subjectBase, profile) ?? $"I{implementationStem}"
        };
    }

    public string ResolveInterfaceFileName(string subjectBase, LayerConventionProfile profile)
    {
        string typeName = ResolveInterfaceTypeName(subjectBase, profile);
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return string.Empty;
        }

        return $"{typeName}.cs";
    }

    private string? ResolveInterfaceTypeNameFromExemplar(string subjectBase, LayerConventionProfile profile)
    {
        if (string.IsNullOrWhiteSpace(ExemplarEntityName)
            || string.IsNullOrWhiteSpace(ExemplarInterfaceTypeName))
        {
            return null;
        }

        string exemplarImplementationStem = $"{ExemplarEntityName}{profile.RoleName}";
        if (!ExemplarInterfaceTypeName.Contains(exemplarImplementationStem, StringComparison.Ordinal))
        {
            return ExemplarInterfaceTypeName.Replace(ExemplarEntityName, subjectBase, StringComparison.Ordinal);
        }

        return ExemplarInterfaceTypeName.Replace(ExemplarEntityName, subjectBase, StringComparison.Ordinal);
    }
}

public sealed record LayerConventionProfile(
    string RoleName,
    string FileSuffix,
    int SampleCount,
    string? CanonicalDirectory,
    bool RequireInheritanceClause,
    bool RequireMatchingRoleInterface,
    bool RequireBaseConstructorCall,
    IReadOnlyList<string> RequiredInheritedTypeTokens,
    IReadOnlyList<string> RequiredConstructorParamTypes,
    LayerInterfacePairingConvention InterfacePairing);

public sealed class LayerConventionProfiles
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
        foreach (LayerConventionProfile profile in GetActiveProfiles())
        {
            if (MatchesImplementationFile(fileName, profile))
            {
                return profile;
            }
        }

        return null;
    }

    public IEnumerable<LayerConventionProfile> GetActiveProfiles() => _profiles;

    public static bool MatchesImplementationFile(string fileName, LayerConventionProfile profile)
    {
        if (!fileName.EndsWith(profile.FileSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (fileName.StartsWith('I'))
        {
            return false;
        }

        string roleBaseFile = profile.RoleName + Path.GetExtension(profile.FileSuffix);
        return !fileName.Equals(roleBaseFile, StringComparison.OrdinalIgnoreCase);
    }

    public static string? GetSubjectBaseName(string fileName, LayerConventionProfile profile)
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

    public static string BuildExpectedInterfaceFileName(string subjectBase, LayerConventionProfile profile) =>
        profile.InterfacePairing.ResolveInterfaceFileName(subjectBase, profile);

    public static string BuildExpectedImplementationFileName(string subjectBase, LayerConventionProfile profile) =>
        $"{subjectBase}{profile.RoleName}.cs";

    public static IEnumerable<string> ResolveRequiredConstructorParamTypes(
        string repoPath,
        string subjectBase,
        LayerConventionProfile profile,
        string? targetRelativePath = null)
    {
        var required = ResolveRequiredConstructorParameters(repoPath, subjectBase, profile, targetRelativePath)
            .Select(parameter => parameter.Type)
            .ToHashSet(StringComparer.Ordinal);

        if (profile.InterfacePairing.LayerUsesInterfaces || profile.RequireMatchingRoleInterface)
        {
            string interfaceTypeName = profile.InterfacePairing.ResolveInterfaceTypeName(subjectBase, profile);
            if (!string.IsNullOrWhiteSpace(interfaceTypeName))
            {
                required.Add(interfaceTypeName);
            }
        }

        return required;
    }

    public static IEnumerable<(string Type, string Name)> ResolveRequiredConstructorParameters(
        string repoPath,
        string subjectBase,
        LayerConventionProfile profile,
        string? targetRelativePath = null)
    {
        var required = new List<(string Type, string Name)>();
        var seenTypes = new HashSet<string>(StringComparer.Ordinal);

        LayerExemplarConstructor? exemplar = FindExemplarConstructor(
            repoPath,
            targetRelativePath,
            profile,
            requireBaseCtorCall: false);
        if (exemplar is null)
        {
            return required;
        }

        string? exemplarSubject = GetSubjectBaseName(Path.GetFileName(exemplar.SourceRelativePath), profile);
        if (string.IsNullOrWhiteSpace(exemplarSubject))
        {
            return required;
        }

        foreach ((string type, string name) in ParseConstructorParameterSpecs(exemplar.ParameterList))
        {
            string mappedType = MapEntityInDependencyType(type, exemplarSubject, subjectBase);
            if (!seenTypes.Add(mappedType))
            {
                continue;
            }

            string mappedName = mappedType.Equals(type, StringComparison.Ordinal)
                ? name
                : ToParameterName(mappedType);
            required.Add((mappedType, mappedName));
        }

        return required;
    }

    public static HashSet<string> ParseConstructorParameterTypes(string content, string className)
    {
        var types = new HashSet<string>(StringComparer.Ordinal);
        foreach (string type in ParseConstructorParameterSpecs(ExtractConstructorParameterList(content, className))
                     .Select(spec => spec.Type))
        {
            types.Add(type);
        }

        return types;
    }

    public static string? TryGetConstructorExemplarRelativePath(
        string repoPath,
        string subjectBase,
        LayerConventionProfile profile,
        string? targetRelativePath = null) =>
        FindExemplarConstructor(repoPath, targetRelativePath, profile, requireBaseCtorCall: false)?.SourceRelativePath;

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

        LayerExemplarConstructor? exemplar = FindExemplarConstructor(
            repoPath,
            relativePath,
            profile,
            requireBaseCtorCall: true);
        if (exemplar is null || MatchesExemplarConstructor(content, className, exemplar))
        {
            return true;
        }

        reason =
            $"Must match layer exemplar {exemplar.SourceRelativePath} "
            + $"(public {className}({exemplar.ParameterList}) : base({exemplar.BaseArgumentList})).";
        return false;
    }

    private static string ExtractConstructorParameterList(string content, string className)
    {
        Match match = ConstructorPatternRegex.Match(content);
        if (!match.Success || !match.Groups["class"].Value.Equals(className, StringComparison.Ordinal))
        {
            return string.Empty;
        }

        return match.Groups["params"].Value.Trim();
    }

    private static IEnumerable<(string Type, string Name)> ParseConstructorParameterSpecs(string parameterList)
    {
        foreach (string param in parameterList.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            string normalized = Regex.Replace(param.Trim(), @"\s+", " ");
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            string[] parts = normalized.Split(' ');
            if (parts.Length >= 2)
            {
                yield return (parts[0].Trim(), parts[^1].Trim());
            }
        }
    }

    private static string ToParameterName(string typeName)
    {
        string stem = typeName.StartsWith('I') && typeName.Length > 1 && char.IsUpper(typeName[1])
            ? typeName[1..]
            : typeName;
        return stem.Length == 0
            ? "dependency"
            : char.ToLowerInvariant(stem[0]) + stem[1..];
    }

    private static string MapEntityInDependencyType(
        string paramType,
        string exemplarEntity,
        string targetEntity) =>
        exemplarEntity.Equals(targetEntity, StringComparison.Ordinal)
            ? paramType
            : paramType.Replace(exemplarEntity, targetEntity, StringComparison.Ordinal);

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
        string? relativePath,
        LayerConventionProfile profile,
        bool requireBaseCtorCall)
    {
        string? directory = null;
        if (!string.IsNullOrWhiteSpace(relativePath))
        {
            directory = Path.GetDirectoryName(
                Path.Combine(repoPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        IEnumerable<string> candidates = !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, $"*{profile.FileSuffix}", SearchOption.TopDirectoryOnly)
            : Directory.EnumerateFiles(repoPath, $"*{profile.FileSuffix}", SearchOption.AllDirectories);

        string? selfFullPath = string.IsNullOrWhiteSpace(relativePath)
            ? null
            : Path.GetFullPath(Path.Combine(repoPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        foreach (string candidate in candidates
                     .Where(path => MatchesImplementationFile(Path.GetFileName(path), profile))
                     .Where(path => !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                                 && !path.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
                     .OrderByDescending(path => GetSubjectBaseName(Path.GetFileName(path), profile)?.Length ?? 0)
                     .ThenBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(selfFullPath)
                && Path.GetFullPath(candidate).Equals(selfFullPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string className = Path.GetFileNameWithoutExtension(candidate);
            Match match = ConstructorPatternRegex.Match(File.ReadAllText(candidate));
            if (!match.Success
                || !match.Groups["class"].Value.Equals(className, StringComparison.Ordinal))
            {
                continue;
            }

            if (requireBaseCtorCall && !match.Groups["baseArgs"].Success)
            {
                continue;
            }

            return new LayerExemplarConstructor(
                Path.GetRelativePath(repoPath, candidate).Replace('\\', '/'),
                match.Groups["params"].Value.Trim(),
                match.Groups["baseArgs"].Success ? match.Groups["baseArgs"].Value.Trim() : string.Empty);
        }

        return null;
    }

    private static readonly Regex ConstructorPatternRegex = new(
        @"public\s+(?<class>[A-Za-z_][A-Za-z0-9_]*)\s*\((?<params>[^)]*)\)\s*(?::\s*base\s*\((?<baseArgs>[^)]*)\))?\s*\{",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private sealed record LayerExemplarConstructor(
        string SourceRelativePath,
        string ParameterList,
        string BaseArgumentList);
}
