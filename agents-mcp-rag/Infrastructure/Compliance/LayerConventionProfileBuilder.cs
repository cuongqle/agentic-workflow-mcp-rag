using System.Text.RegularExpressions;

static class LayerConventionProfileBuilder
{
    public static LayerConventionProfiles Build(string repoPath)
    {
        return new LayerConventionProfiles(
            BuildRoleProfile(repoPath, "Repository", "Repository.cs", skipBaseFileName: "Repository.cs"),
            BuildRoleProfile(repoPath, "Service", "Service.cs", skipBaseFileName: "Service.cs"),
            BuildRoleProfile(repoPath, "Controller", "Controller.cs", skipBaseFileName: null));
    }

    private static LayerConventionProfile? BuildRoleProfile(string repoPath, string roleName, string suffix, string? skipBaseFileName)
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
                constructorParamTypeCount[ctorType] = constructorParamTypeCount.TryGetValue(ctorType, out int count) ? count + 1 : 1;
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

        return new LayerConventionProfile(
            RoleName: roleName,
            FileSuffix: suffix,
            SampleCount: sampleCount,
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
        bool hasMatchingRoleInterface = inheritedTypes.Any(type => type.Equals(expectedRoleInterface, StringComparison.Ordinal));
        bool hasBaseCtorCall = content.Contains("base(", StringComparison.Ordinal);

        var ctorParamTypes = new HashSet<string>(StringComparer.Ordinal);
        var ctorMatches = Regex.Matches(content, $@"public\s+{Regex.Escape(className)}\s*\(([^)]*)\)");
        foreach (Match ctorMatch in ctorMatches)
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
    public LayerConventionProfiles(
        LayerConventionProfile? repository,
        LayerConventionProfile? service,
        LayerConventionProfile? controller)
    {
        Repository = repository;
        Service = service;
        Controller = controller;
    }

    public LayerConventionProfile? Repository { get; }
    public LayerConventionProfile? Service { get; }
    public LayerConventionProfile? Controller { get; }

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

    public IEnumerable<LayerConventionProfile> GetActiveProfiles()
    {
        if (Repository is not null)
        {
            yield return Repository;
        }

        if (Service is not null)
        {
            yield return Service;
        }

        if (Controller is not null)
        {
            yield return Controller;
        }
    }

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
        if (!profile.RoleName.Equals("Controller", StringComparison.Ordinal))
        {
            return profile.RequiredConstructorParamTypes;
        }

        var required = new HashSet<string>(StringComparer.Ordinal)
        {
            $"I{subjectBase}Repository"
        };

        string? exemplarPath = Directory
            .EnumerateFiles(repoPath, $"{subjectBase}Controller.cs", SearchOption.AllDirectories)
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
}

sealed record LayerConventionProfile(
    string RoleName,
    string FileSuffix,
    int SampleCount,
    bool RequireInheritanceClause,
    bool RequireMatchingRoleInterface,
    bool RequireBaseConstructorCall,
    IReadOnlyList<string> RequiredInheritedTypeTokens,
    IReadOnlyList<string> RequiredConstructorParamTypes);
