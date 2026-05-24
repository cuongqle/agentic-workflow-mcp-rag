namespace agents_mcp_rag.Infrastructure;

/// <summary>
/// Discovers frontend (JS/TS module layout) repo contract signals.
/// </summary>
internal static class FrontendRepoContractDiscoverer
{
    private static readonly string[] FeatureModuleSubfolderProbeNames =
    [
        "controllers", "views", "services", "proxies", "components", "pages", "hooks", "modules", "routes"
    ];

    internal static FrontendRepoContractSignals Discover(string repoPath)
    {
        if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
        {
            return FrontendRepoContractSignals.Empty;
        }

        return new FrontendRepoContractSignals(DiscoverModuleTemplate(repoPath));
    }

    private static FrontendModuleTemplate? DiscoverModuleTemplate(string repoPath)
    {
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

        string? npmProjectRoot = DiscoverNpmProjectRoot(repoPath, webProjectRoot, best.ModulesRoot);

        return new FrontendModuleTemplate(
            best.ModulesRoot,
            webProjectRoot,
            exemplarModule,
            layoutMode,
            forbidden,
            requiredSubfolders,
            allowedRootFiles,
            exemplarFilePaths,
            npmProjectRoot);
    }

    private static string? DiscoverNpmProjectRoot(string repoPath, string webProjectRoot, string modulesRoot)
    {
        foreach (string relativeRoot in CandidateNpmRoots(webProjectRoot, modulesRoot))
        {
            string absoluteRoot = Path.Combine(repoPath, relativeRoot.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(Path.Combine(absoluteRoot, "package.json")))
            {
                return relativeRoot;
            }
        }

        return null;
    }

    private static IEnumerable<string> CandidateNpmRoots(string webProjectRoot, string modulesRoot)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(webProjectRoot))
        {
            candidates.Add(webProjectRoot.Replace('\\', '/').Trim('/'));
        }

        if (!string.IsNullOrWhiteSpace(modulesRoot))
        {
            string normalizedModules = modulesRoot.Replace('\\', '/').Trim('/');
            candidates.Add(normalizedModules);

            string? parent = Path.GetDirectoryName(normalizedModules.Replace('/', Path.DirectorySeparatorChar))
                ?.Replace('\\', '/');
            if (!string.IsNullOrWhiteSpace(parent))
            {
                candidates.Add(parent.Trim('/'));
            }
        }

        return candidates.Distinct(StringComparer.OrdinalIgnoreCase);
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

internal sealed record FrontendRepoContractSignals(FrontendModuleTemplate? ModuleTemplate)
{
    public static FrontendRepoContractSignals Empty { get; } = new(ModuleTemplate: null);

    public bool IsDiscovered => ModuleTemplate is not null;
}
