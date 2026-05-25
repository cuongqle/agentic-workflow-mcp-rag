using System.Text.RegularExpressions;

namespace workflowX.Infrastructure;

/// <summary>
/// Discovers .NET-specific repo contract signals (paths, layers, DI, composition roots).
/// </summary>
internal static class DotNetRepoContractDiscoverer
{
    internal static DotNetRepoContractSignals Discover(string repoPath)
    {
        if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
        {
            return DotNetRepoContractSignals.Empty;
        }

        var pathRules = BuildPathRules(repoPath);
        return new DotNetRepoContractSignals(
            PathRules: pathRules,
            LayerConventions: LayerConventionProfileBuilder.Build(repoPath),
            Entity: DiscoverEntityConvention(repoPath),
            RepositoryInterfacesNamespace: ResolveRepositoryInterfacesNamespace(repoPath, pathRules),
            ConsumerSuffixes: TypeMemberConsistencyGuard.DiscoverConsumerSuffixes(repoPath),
            CompositionRootPaths: DiscoverCompositionRootPaths(repoPath),
            RegistrationScope: RegistrationScopeDiscoverer.Discover(repoPath));
    }

    internal static IReadOnlyList<string> DiscoverCompositionRootPaths(string repoPath)
    {
        if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
        {
            return Array.Empty<string>();
        }

        return Directory
            .EnumerateFiles(repoPath, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsExcludedBuildPath(path))
            .Select(path => Path.GetRelativePath(repoPath, path).Replace('\\', '/'))
            .Where(DependencyWiringAuditor.IsCompositionRootPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static string? DetectCanonicalDirectoryForFileSuffix(
        string repoPath,
        string fileSuffix,
        string? preferredDirectoryName = null)
    {
        var matchingFiles = Directory.EnumerateFiles(repoPath, $"*{fileSuffix}", SearchOption.AllDirectories)
            .Where(path => !IsExcludedBuildPath(path))
            .ToList();
        if (matchingFiles.Count == 0)
        {
            return null;
        }

        return matchingFiles
            .Select(path => Path.GetRelativePath(repoPath, Path.GetDirectoryName(path) ?? string.Empty).Replace('\\', '/'))
            .Where(relative => !string.IsNullOrWhiteSpace(relative))
            .GroupBy(relative => relative, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Directory = group.Key,
                Count = group.Count(),
                IsPreferred = !string.IsNullOrWhiteSpace(preferredDirectoryName)
                              && (group.Key.EndsWith("/" + preferredDirectoryName, StringComparison.OrdinalIgnoreCase)
                                  || group.Key.Equals(preferredDirectoryName, StringComparison.OrdinalIgnoreCase)
                                  || group.Key.Contains("/" + preferredDirectoryName + "/", StringComparison.OrdinalIgnoreCase))
            })
            .OrderByDescending(entry => entry.Count)
            .ThenByDescending(entry => entry.IsPreferred)
            .ThenBy(entry => entry.Directory.Length)
            .ThenBy(entry => entry.Directory, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()
            ?.Directory;
    }

    private static List<PathPlacementRule> BuildPathRules(string repoPath)
    {
        var rules = new List<PathPlacementRule>();
        AddPlacementRule(rules, repoPath, "Controller.cs");
        AddPlacementRule(
            rules,
            repoPath,
            "Repository.cs",
            fileName => !fileName.StartsWith('I') && !fileName.Equals("Repository.cs", StringComparison.OrdinalIgnoreCase));
        AddPlacementRule(rules, repoPath, "Index.cs");
        AddPlacementRule(rules, repoPath, "Tests.cs");

        string? interfacesDir = DetectCanonicalDirectoryForFileSuffix(repoPath, "Repository.cs", "Interfaces")
                                  ?? DetectCanonicalDirectoryForFileSuffix(repoPath, "IRepository.cs", "Interfaces");
        if (!string.IsNullOrWhiteSpace(interfacesDir))
        {
            rules.Add(new PathPlacementRule(
                "Repository.cs",
                interfacesDir,
                fileName => fileName.StartsWith('I') && !fileName.Equals("IRepository.cs", StringComparison.OrdinalIgnoreCase)));
        }

        string? entitiesDir = DetectCanonicalDirectoryForFileSuffix(repoPath, ".cs", "Entities");
        if (!string.IsNullOrWhiteSpace(entitiesDir))
        {
            rules.Add(new PathPlacementRule(".cs", entitiesDir, IsEntityLikeFileName));
        }

        foreach (var convention in TestCoverageAuditor.DiscoverTestConventions(repoPath))
        {
            if (!string.IsNullOrWhiteSpace(convention.TestDirectory))
            {
                rules.Add(new PathPlacementRule("Tests.cs", convention.TestDirectory, null));
            }
        }

        return rules;
    }

    private static EntityConvention? DiscoverEntityConvention(string repoPath)
    {
        var entityFiles = Directory
            .EnumerateFiles(repoPath, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsExcludedBuildPath(path))
            .Where(path => IsEntityLikeFileName(Path.GetFileName(path)))
            .Where(path => path.Contains("Entities", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (entityFiles.Count < 2)
        {
            return null;
        }

        var interfaceCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string file in entityFiles)
        {
            string content = File.ReadAllText(file);
            Match match = Regex.Match(content, @"public\s+class\s+[A-Za-z_][A-Za-z0-9_]*\s*:\s*([A-Za-z_][A-Za-z0-9_]*)");
            if (match.Success)
            {
                string iface = match.Groups[1].Value;
                interfaceCounts[iface] = interfaceCounts.TryGetValue(iface, out int count) ? count + 1 : 1;
            }
        }

        var dominant = interfaceCounts
            .OrderByDescending(kvp => kvp.Value)
            .FirstOrDefault();
        if (dominant.Key is null || (double)dominant.Value / entityFiles.Count < 0.7)
        {
            return null;
        }

        string? exemplarPath = entityFiles.FirstOrDefault(path =>
            File.ReadAllText(path).Contains($": {dominant.Key}", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(exemplarPath))
        {
            return null;
        }

        string exemplarRelative = Path.GetRelativePath(repoPath, exemplarPath).Replace('\\', '/');
        string canonicalDirectory = Path.GetRelativePath(
                repoPath,
                Path.GetDirectoryName(exemplarPath) ?? repoPath)
            .Replace('\\', '/');
        string? requiredUsing = File.ReadLines(exemplarPath)
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith("using ", StringComparison.Ordinal));

        return new EntityConvention(
            canonicalDirectory,
            dominant.Key,
            exemplarRelative,
            requiredUsing);
    }

    private static string? ResolveRepositoryInterfacesNamespace(string repoPath, IReadOnlyList<PathPlacementRule> rules)
    {
        string? ifaceDir = rules
            .FirstOrDefault(rule => rule.FileFilter is not null && rule.Directory.Contains("Interfaces", StringComparison.OrdinalIgnoreCase))
            ?.Directory;
        if (string.IsNullOrWhiteSpace(ifaceDir))
        {
            return null;
        }

        string interfacesAbsolute = Path.Combine(repoPath, ifaceDir.Replace('/', Path.DirectorySeparatorChar));
        string? sampleInterfaceFile = Directory
            .EnumerateFiles(interfacesAbsolute, "I*Repository.cs", SearchOption.TopDirectoryOnly)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(sampleInterfaceFile))
        {
            return null;
        }

        return File.ReadLines(sampleInterfaceFile)
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith("namespace ", StringComparison.Ordinal))
            ?.Substring("namespace ".Length)
            .Trim();
    }

    private static void AddPlacementRule(
        List<PathPlacementRule> rules,
        string repoPath,
        string fileSuffix,
        Func<string, bool>? fileFilter = null)
    {
        string? directory = DetectCanonicalDirectoryForFileSuffix(repoPath, fileSuffix);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        if (rules.Any(rule =>
                rule.FileSuffix.Equals(fileSuffix, StringComparison.OrdinalIgnoreCase)
                && rule.Directory.Equals(directory, StringComparison.OrdinalIgnoreCase)
                && rule.FileFilter == fileFilter))
        {
            return;
        }

        rules.Add(new PathPlacementRule(fileSuffix, directory, fileFilter));
    }

    private static bool IsExcludedBuildPath(string absolutePath)
    {
        string normalized = absolutePath.Replace('\\', '/');
        return normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsEntityLikeFileName(string fileName) =>
        !fileName.StartsWith('I')
        && !fileName.EndsWith("Repository.cs", StringComparison.OrdinalIgnoreCase)
        && !fileName.EndsWith("Controller.cs", StringComparison.OrdinalIgnoreCase)
        && !fileName.EndsWith("Service.cs", StringComparison.OrdinalIgnoreCase)
        && !fileName.EndsWith("Index.cs", StringComparison.OrdinalIgnoreCase)
        && !fileName.EndsWith("Expression.cs", StringComparison.OrdinalIgnoreCase)
        && !fileName.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase);
}

internal sealed record DotNetRepoContractSignals(
    IReadOnlyList<PathPlacementRule> PathRules,
    LayerConventionProfiles LayerConventions,
    EntityConvention? Entity,
    string? RepositoryInterfacesNamespace,
    IReadOnlyList<string> ConsumerSuffixes,
    IReadOnlyList<string> CompositionRootPaths,
    RegistrationScopeConvention RegistrationScope)
{
    public static DotNetRepoContractSignals Empty { get; } = new(
        Array.Empty<PathPlacementRule>(),
        LayerConventionProfiles.Empty,
        null,
        null,
        Array.Empty<string>(),
        Array.Empty<string>(),
        RegistrationScopeConvention.None);
}
