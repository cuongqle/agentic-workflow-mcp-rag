using System.Text.RegularExpressions;

namespace agents_mcp_rag.Infrastructure;

internal static class RepoContractDiscoverer
{
    private static readonly string[] FeatureModuleSubfolderProbeNames =
    [
        "controllers", "views", "services", "proxies", "components", "pages", "hooks", "modules", "routes"
    ];

    public static RepoContract Discover(string repoPath)
    {
        if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
        {
            return new RepoContract
            {
                RepoPath = repoPath ?? string.Empty,
                LayerConventions = LayerConventionProfiles.Empty
            };
        }

        var pathRules = BuildPathRules(repoPath);
        FrontendModuleTemplate? frontend = DiscoverFrontendModuleTemplate(repoPath);
        string? repositoryInterfacesNamespace = ResolveRepositoryInterfacesNamespace(repoPath, pathRules);
        LayerConventionProfiles layerConventions = LayerConventionProfileBuilder.Build(repoPath);
        EntityConvention? entityConvention = DiscoverEntityConvention(repoPath);
        IReadOnlyList<string> consumerSuffixes = TypeMemberConsistencyGuard.DiscoverConsumerSuffixes(repoPath);
        var compositionRoots = Directory
            .EnumerateFiles(repoPath, "*.cs", SearchOption.AllDirectories)
            .Where(path => DependencyWiringAuditor.IsCompositionRootPath(
                Path.GetRelativePath(repoPath, path).Replace('\\', '/')))
            .Select(path => Path.GetRelativePath(repoPath, path).Replace('\\', '/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new RepoContract
        {
            RepoPath = repoPath,
            PathRules = pathRules,
            Frontend = frontend,
            LayerConventions = layerConventions,
            Entity = entityConvention,
            RepositoryInterfacesNamespace = repositoryInterfacesNamespace,
            ConsumerSuffixes = consumerSuffixes,
            CompositionRootPaths = compositionRoots,
            RegistrationScope = RegistrationScopeDiscoverer.Discover(repoPath)
        };
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

    private static FrontendModuleTemplate? DiscoverFrontendModuleTemplate(string repoPath)
    {
        if (!Directory.Exists(repoPath))
        {
            return null;
        }

        var candidates = new List<(string ModulesRoot, int Score)>();
        foreach (string modulesDir in Directory.EnumerateDirectories(repoPath, "modules", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(repoPath, modulesDir).Replace('\\', '/');
            if (relative.Contains("/Scripts/", StringComparison.OrdinalIgnoreCase)
                || relative.Contains("/node_modules/", StringComparison.OrdinalIgnoreCase)
                || !HasFeatureModuleChildren(modulesDir))
            {
                continue;
            }

            int jsCount = Directory
                .EnumerateFiles(modulesDir, "*.*", SearchOption.AllDirectories)
                .Count(path =>
                {
                    string ext = Path.GetExtension(path);
                    return ext.Equals(".js", StringComparison.OrdinalIgnoreCase)
                           || ext.Equals(".ts", StringComparison.OrdinalIgnoreCase)
                           || ext.Equals(".tsx", StringComparison.OrdinalIgnoreCase)
                           || ext.Equals(".jsx", StringComparison.OrdinalIgnoreCase)
                           || ext.Equals(".vue", StringComparison.OrdinalIgnoreCase)
                           || ext.Equals(".html", StringComparison.OrdinalIgnoreCase);
                });
            int score = jsCount;
            string? webProject = ResolveWebProjectRoot(repoPath, modulesDir);
            if (!string.IsNullOrWhiteSpace(webProject))
            {
                score += 500;
            }

            if (IsSolutionSiblingApplicationFolder(relative))
            {
                score -= 400;
            }

            candidates.Add((relative, score));
        }

        var best = candidates.OrderByDescending(c => c.Score).ThenBy(c => c.ModulesRoot.Length).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(best.ModulesRoot))
        {
            return null;
        }

        string modulesAbsolute = Path.Combine(repoPath, best.ModulesRoot.Replace('/', Path.DirectorySeparatorChar));
        string exemplarModule = Directory.EnumerateDirectories(modulesAbsolute)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderByDescending(name => Directory.EnumerateFiles(Path.Combine(modulesAbsolute, name!), "*.js", SearchOption.AllDirectories).Count())
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault() ?? "sample";

        string webProjectRoot = ResolveWebProjectRoot(repoPath, modulesAbsolute)
                                ?? Path.GetRelativePath(repoPath, Path.GetDirectoryName(Path.GetDirectoryName(modulesAbsolute) ?? modulesAbsolute) ?? modulesAbsolute)
                                    .Replace('\\', '/');

        var forbidden = Directory.EnumerateDirectories(repoPath)
            .Select(path => Path.GetRelativePath(repoPath, path).Replace('\\', '/'))
            .Where(relative =>
                relative.EndsWith(".Application", StringComparison.OrdinalIgnoreCase)
                && !webProjectRoot.StartsWith(relative + "/", StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(Path.Combine(repoPath, relative.Replace('/', Path.DirectorySeparatorChar), "modules")))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        string exemplarAbsolute = Path.Combine(modulesAbsolute, exemplarModule);
        var requiredSubfolders = Directory.Exists(exemplarAbsolute)
            ? Directory.EnumerateDirectories(exemplarAbsolute)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Cast<string>()
                .ToList()
            : new List<string>();

        var allowedRootFiles = Directory.Exists(exemplarAbsolute)
            ? Directory.EnumerateFiles(exemplarAbsolute)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Cast<string>()
                .ToList()
            : new List<string>();

        var exemplarFilePaths = Directory.Exists(exemplarAbsolute)
            ? Directory.EnumerateFiles(exemplarAbsolute, "*.*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(repoPath, path).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : new List<string>();

        FrontendLayoutMode layoutMode = DetectFrontendLayoutMode(modulesAbsolute);

        return new FrontendModuleTemplate(
            best.ModulesRoot,
            webProjectRoot,
            exemplarModule,
            layoutMode,
            forbidden,
            requiredSubfolders,
            allowedRootFiles,
            exemplarFilePaths);
    }

    private static EntityConvention? DiscoverEntityConvention(string repoPath)
    {
        var entityFiles = Directory
            .EnumerateFiles(repoPath, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                        && !path.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
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

    private static FrontendLayoutMode DetectFrontendLayoutMode(string modulesAbsolute)
    {
        int bootstrapModuleCount = Directory
            .EnumerateDirectories(modulesAbsolute)
            .Count(moduleDir =>
                File.Exists(Path.Combine(moduleDir, "loader.js"))
                && File.Exists(Path.Combine(moduleDir, "router.js")));

        return bootstrapModuleCount <= 1
            ? FrontendLayoutMode.HostModulePages
            : FrontendLayoutMode.SiblingFeatureModules;
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

    internal static string? DetectCanonicalDirectoryForFileSuffix(
        string repoPath,
        string fileSuffix,
        string? preferredDirectoryName = null)
    {
        var matchingFiles = Directory.EnumerateFiles(repoPath, $"*{fileSuffix}", SearchOption.AllDirectories)
            .Where(path => !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                        && !path.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
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

    private static bool IsEntityLikeFileName(string fileName) =>
        !fileName.StartsWith('I')
        && !fileName.EndsWith("Repository.cs", StringComparison.OrdinalIgnoreCase)
        && !fileName.EndsWith("Controller.cs", StringComparison.OrdinalIgnoreCase)
        && !fileName.EndsWith("Service.cs", StringComparison.OrdinalIgnoreCase)
        && !fileName.EndsWith("Index.cs", StringComparison.OrdinalIgnoreCase)
        && !fileName.EndsWith("Expression.cs", StringComparison.OrdinalIgnoreCase)
        && !fileName.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase);

    private static bool HasFeatureModuleChildren(string modulesDir) =>
        Directory.EnumerateDirectories(modulesDir)
            .Any(moduleDir => FeatureModuleSubfolderProbeNames.Any(name =>
                Directory.Exists(Path.Combine(moduleDir, name))));

    private static bool IsSolutionSiblingApplicationFolder(string modulesRelativePath)
    {
        string[] segments = modulesRelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length >= 2
               && segments[0].EndsWith(".Application", StringComparison.OrdinalIgnoreCase)
               && segments[1].Equals("modules", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveWebProjectRoot(string repoPath, string modulesAbsoluteDir)
    {
        string? current = Path.GetDirectoryName(modulesAbsoluteDir);
        for (int depth = 0; depth < 4 && !string.IsNullOrWhiteSpace(current); depth++)
        {
            string? csproj = Directory.EnumerateFiles(current, "*.csproj", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(path =>
                    !path.Contains("Test", StringComparison.OrdinalIgnoreCase)
                    && !path.Contains(".UnitTest", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(csproj))
            {
                return Path.GetRelativePath(repoPath, Path.GetDirectoryName(csproj) ?? current).Replace('\\', '/');
            }

            current = Path.GetDirectoryName(current);
        }

        return null;
    }
}
