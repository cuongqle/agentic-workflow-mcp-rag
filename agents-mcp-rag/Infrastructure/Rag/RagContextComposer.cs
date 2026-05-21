using System.Text;
using System.Text.RegularExpressions;
using agents_mcp_rag.Infrastructure;

static class RagContextComposer
{
    public static Task<RagContextBundle> BuildAsync(string repoPath, string taskPrompt, CodebaseRagIndex ragIndex)
    {
        string structureContext = ClampContext(BuildStructureContext(repoPath, ragIndex), 4_000);
        string legacyContext = ClampContext(BuildLegacyImplementationContext(repoPath, taskPrompt, ragIndex), 10_000);

        var combined = new StringBuilder();
        combined.AppendLine("Unified RAG context (structure + implementation patterns):");
        combined.AppendLine();
        combined.AppendLine("=== Structure ===");
        combined.AppendLine(structureContext);
        combined.AppendLine();
        combined.AppendLine("=== Legacy/Pattern Conventions ===");
        combined.AppendLine(legacyContext);

        string combinedContext = ClampContext(combined.ToString(), 14_000);
        return Task.FromResult(new RagContextBundle(structureContext, legacyContext, combinedContext));
    }

    private static string BuildStructureContext(string repoPath, CodebaseRagIndex ragIndex)
    {
        var content = new StringBuilder();
        content.AppendLine("Repository structure profile:");
        content.AppendLine($"- Root: {repoPath}");

        if (!Directory.Exists(repoPath))
        {
            content.AppendLine("- Repository path not found.");
            return content.ToString();
        }

        var files = RepoCodeFileScanner.EnumerateRelevantFiles(repoPath).ToList();
        content.AppendLine($"- Relevant source/config files detected: {files.Count}");
        content.AppendLine($"- Backend controller roots: {FormatList(FindDirectoriesEndingWith(repoPath, "Controllers"))}");
        content.AppendLine($"- Backend entity/model roots: {FormatList(FindDirectoriesEndingWith(repoPath, "Entities", "Models"))}");
        FrontendLayout? frontend = DiscoverFrontendLayout(repoPath);
        if (frontend is not null)
        {
            content.AppendLine($"- Frontend host project root: {frontend.WebProjectRoot}");
            content.AppendLine($"- Frontend feature-modules root (authoritative): {frontend.ModulesRoot}");
            content.AppendLine($"- Frontend exemplar feature: {frontend.ExemplarModuleName}");
            if (frontend.RequiredSubfolders.Count > 0)
            {
                content.AppendLine($"- Required feature subfolders: {FormatList(frontend.RequiredSubfolders)}");
            }
            if (frontend.AllowedRootFileNames.Count > 0)
            {
                content.AppendLine(
                    $"- Files allowed at feature root only: {FormatList(frontend.AllowedRootFileNames)}");
            }
            if (frontend.ForbiddenRoots.Count > 0)
            {
                content.AppendLine($"- Do NOT add parallel frontend roots: {FormatList(frontend.ForbiddenRoots)}");
            }
        }
        else
        {
            content.AppendLine($"- Frontend controller roots: {FormatList(FindDirectoriesContaining(repoPath, "controllers"))}");
            content.AppendLine($"- Frontend service roots: {FormatList(FindDirectoriesContaining(repoPath, "services"))}");
            content.AppendLine($"- Frontend view roots: {FormatList(FindDirectoriesContaining(repoPath, "views"))}");
        }

        var representative = files
            .Select(path => Path.GetRelativePath(repoPath, path))
            .OrderBy(path => path.Length)
            .ThenBy(path => path)
            .Take(10)
            .ToList();
        if (representative.Count > 0)
        {
            content.AppendLine("- Representative files:");
            foreach (var file in representative)
            {
                content.AppendLine($"  - {file.Replace('\\', '/')}");
            }
        }

        var ragSignals = ReadRagSignals(ragIndex);
        if (ragSignals.Count > 0)
        {
            content.AppendLine("- RAG structure signals:");
            foreach (var signal in ragSignals)
            {
                content.AppendLine($"  - {signal}");
            }
        }

        return content.ToString();
    }

    private static string BuildLegacyImplementationContext(string repoPath, string taskPrompt, CodebaseRagIndex ragIndex)
    {
        if (!Directory.Exists(repoPath))
        {
            return "Legacy implementation context unavailable: repository path not found.";
        }

        var sb = new StringBuilder();
        sb.AppendLine("Legacy implementation exemplars (use these conventions):");

        var signals = ExtractTaskSignals(taskPrompt);
        string entityName = InferTargetEntityName(taskPrompt) ?? "NewEntity";
        var candidateFiles = RepoCodeFileScanner.EnumerateRelevantFiles(repoPath).ToList();
        FrontendLayout? frontend = DiscoverFrontendLayout(repoPath);

        AppendCorpusSummary(sb, candidateFiles, repoPath);
        AppendSemanticContext(sb, ragIndex, taskPrompt);

        var ranked = candidateFiles
            .Select(path => new
            {
                Path = path,
                Score = ScoreFile(Path.GetRelativePath(repoPath, path).Replace('\\', '/'), signals, frontend)
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Path.Length)
            .Take(14)
            .Select(x => x.Path)
            .ToList();

        AppendCategory(sb, "WebAPI controllers", ranked, path => path.Contains("Controller", StringComparison.OrdinalIgnoreCase) && path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase), repoPath);
        AppendCategory(sb, "Repository/entities/indexes", ranked, path =>
            path.Contains("Repository", StringComparison.OrdinalIgnoreCase)
            || path.Contains("Entities", StringComparison.OrdinalIgnoreCase)
            || path.Contains("Index", StringComparison.OrdinalIgnoreCase), repoPath);
        AppendCategory(sb, "Frontend UI modules", ranked, path =>
            FeatureModuleSubfolderNames.Any(name => path.Contains($"/{name}/", StringComparison.OrdinalIgnoreCase))
            || path.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".vue", StringComparison.OrdinalIgnoreCase), repoPath);
        AppendCategory(sb, "Unit tests", ranked, path =>
            path.Contains("UnitTest", StringComparison.OrdinalIgnoreCase)
            || path.Contains("Tests", StringComparison.OrdinalIgnoreCase), repoPath);

        AppendRepositoryConventions(sb, repoPath);
        AppendRepositoryImplementationConventions(sb, repoPath);
        AppendRepositoryUsingConventions(sb, repoPath);
        CodeExemplarContext.AppendDiscoveredExemplars(sb, repoPath, taskPrompt);
        string? wiringContext = DependencyWiringAuditor.BuildRegistrationContext(repoPath);
        if (!string.IsNullOrWhiteSpace(wiringContext))
        {
            sb.AppendLine();
            sb.AppendLine(wiringContext);
        }

        string? bootstrapContext = TestBootstrapContext.BuildContext(repoPath);
        if (!string.IsNullOrWhiteSpace(bootstrapContext))
        {
            sb.AppendLine();
            sb.AppendLine(bootstrapContext);
        }
        AppendCriticalRepositoryContract(sb, repoPath, taskPrompt, entityName);
        AppendRepositoryQueryPatterns(sb, repoPath);
        AppendInheritedLayerMembers(sb, repoPath, "Repository");
        AppendControllerPatterns(sb, repoPath);
        AppendInheritedLayerMembers(sb, repoPath, "Controller");
        string? interfaceImplRules = InterfaceImplementationGuard.BuildRagContext(repoPath, taskPrompt);
        if (!string.IsNullOrWhiteSpace(interfaceImplRules))
        {
            sb.AppendLine();
            sb.AppendLine(interfaceImplRules);
        }

        string? typeMemberRules = TypeMemberConsistencyGuard.BuildRagContext(repoPath, taskPrompt);
        if (!string.IsNullOrWhiteSpace(typeMemberRules))
        {
            sb.AppendLine();
            sb.AppendLine(typeMemberRules);
        }
        AppendExpectedPaths(sb, repoPath, entityName);
        AppendFrontendExpectedPaths(sb, repoPath, entityName);
        AppendFrontendModuleStructure(sb, repoPath);

        sb.AppendLine("Required generated file set for new entity:");
        sb.AppendLine("- WebAPI controller");
        sb.AppendLine("- Repository interface (e.g. I{Entity}Repository)");
        sb.AppendLine("- Repository implementation (e.g. {Entity}Repository)");
        sb.AppendLine("- Repository entity model");
        sb.AppendLine("- Repository index");
        sb.AppendLine("- Frontend UI files under the discovered feature-modules root inside the existing host project (never a new sibling project folder that mirrors backend naming)");
        sb.AppendLine("- Unit tests for repository/controller paths");
        sb.AppendLine();
        sb.AppendLine("Required repository interface style:");
        sb.AppendLine($"- public interface I{entityName}Repository: IRepository<{entityName}>");
        sb.AppendLine("Required repository implementation style:");
        sb.AppendLine($"- public class {entityName}Repository: Repository<{entityName}>, I{entityName}Repository");
        sb.AppendLine($"- constructor: public {entityName}Repository(IDbStore dbStore) : base(dbStore)");

        return sb.ToString();
    }

    private static List<string> ReadRagSignals(CodebaseRagIndex ragIndex)
    {
        return ragIndex
            .Search("project structure folders controllers services views entities repository", 8)
            .Select(result => result.Source)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> FindDirectoriesEndingWith(string repoPath, params string[] suffixes)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in Directory.EnumerateDirectories(repoPath, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(repoPath, directory).Replace('\\', '/');
            if (suffixes.Any(suffix => relative.EndsWith($"/{suffix}", StringComparison.OrdinalIgnoreCase) || relative.Equals(suffix, StringComparison.OrdinalIgnoreCase)))
            {
                result.Add(relative);
            }
        }

        return result.OrderBy(x => x).Take(10).ToList();
    }

    private static List<string> FindDirectoriesContaining(string repoPath, string token)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in Directory.EnumerateDirectories(repoPath, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(repoPath, directory).Replace('\\', '/');
            if (relative.Contains($"/{token}", StringComparison.OrdinalIgnoreCase) || relative.EndsWith(token, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(relative);
            }
        }

        return result.OrderBy(x => x).Take(10).ToList();
    }

    private static string FormatList(IReadOnlyList<string> values)
    {
        return values.Count == 0 ? "none" : string.Join(", ", values.Select(v => v.Replace('\\', '/')));
    }

    private static void AppendCorpusSummary(StringBuilder sb, IReadOnlyList<string> files, string repoPath)
    {
        sb.AppendLine();
        sb.AppendLine("Repository-wide pattern summary:");
        sb.AppendLine($"- Total relevant files indexed: {files.Count}");

        var byExtension = files
            .GroupBy(path => Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .Take(8)
            .Select(group => $"{group.Key}: {group.Count()}")
            .ToList();
        if (byExtension.Count > 0)
        {
            sb.AppendLine($"- File types: {string.Join(", ", byExtension)}");
        }

        var topRoots = files
            .Select(path => Path.GetRelativePath(repoPath, path).Replace('\\', '/'))
            .Select(relative => relative.Split('/').FirstOrDefault() ?? string.Empty)
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .GroupBy(root => root, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .Take(10)
            .Select(group => $"{group.Key} ({group.Count()})")
            .ToList();
        if (topRoots.Count > 0)
        {
            sb.AppendLine($"- Top directory roots: {string.Join(", ", topRoots)}");
        }
    }

    private static void AppendSemanticContext(StringBuilder sb, CodebaseRagIndex ragIndex, string taskPrompt)
    {
        var semanticQueries = new List<string>
        {
            taskPrompt,
            "coding style naming patterns syntax conventions repository implementation examples",
            "controller service model patterns validation and error handling",
            "unit tests integration tests mocking assertions conventions"
        };

        var seenSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var collected = new List<(string Source, string Snippet)>();

        foreach (var query in semanticQueries)
        {
            foreach (var result in ragIndex.Search(query, 8))
            {
                string source = result.Source;
                if (!seenSources.Add(source))
                {
                    continue;
                }

                string snippet = result.Text;
                if (string.IsNullOrWhiteSpace(snippet))
                {
                    continue;
                }

                snippet = snippet.Length > 350 ? snippet[..350] : snippet;
                collected.Add((source, snippet));
                if (collected.Count >= 10)
                {
                    break;
                }
            }

            if (collected.Count >= 10)
            {
                break;
            }
        }

        if (collected.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine("Semantic code retrieval hits:");
        foreach (var item in collected)
        {
            sb.AppendLine($"- {item.Source}");
            sb.AppendLine("  Snippet:");
            sb.AppendLine(Indent(item.Snippet, "    "));
        }
    }

    private static void AppendRepositoryConventions(StringBuilder sb, string repoPath)
    {
        var interfaceFiles = Directory.GetFiles(repoPath, "I*Repository.cs", SearchOption.AllDirectories)
            .Where(path => path.Contains("Interfaces", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (interfaceFiles.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine("Repository interface/implementation conventions:");

        foreach (var interfaceFile in interfaceFiles.Take(4))
        {
            string interfaceName = Path.GetFileNameWithoutExtension(interfaceFile);
            string implementationName = interfaceName.TrimStart('I');
            string? implementationPath = Directory.GetFiles(repoPath, $"{implementationName}.cs", SearchOption.AllDirectories)
                .FirstOrDefault(path => path.Contains("Repository", StringComparison.OrdinalIgnoreCase)
                                     && !path.Contains("Interfaces", StringComparison.OrdinalIgnoreCase));

            sb.AppendLine($"- Interface: {Path.GetRelativePath(repoPath, interfaceFile).Replace('\\', '/')}");
            sb.AppendLine($"  Interface signature example: {ExtractInterfaceSignature(interfaceFile)}");
            if (!string.IsNullOrWhiteSpace(implementationPath))
            {
                sb.AppendLine($"  Implementation: {Path.GetRelativePath(repoPath, implementationPath).Replace('\\', '/')}");
            }
        }
    }

    private static string ExtractInterfaceSignature(string interfaceFile)
    {
        foreach (var line in File.ReadLines(interfaceFile))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("public interface ", StringComparison.Ordinal))
            {
                return trimmed;
            }
        }

        return "public interface IExampleRepository: IRepository<Example>";
    }

    private static void AppendExpectedPaths(StringBuilder sb, string repoPath, string entityName)
    {
        string? interfaceDir = FindRepositoryInterfacesDir(repoPath);
        string? repositoryDir = FindRepositoryImplementationDir(repoPath);
        string? entitiesDir = FindRepositoryEntitiesDir(repoPath);
        string? indexesDir = FindRepositoryIndexesDir(repoPath);
        string? controllersDir = FindWebApiControllersDir(repoPath);
        string? testsDir = FindDirectory(repoPath, "RepositoryTest");

        sb.AppendLine();
        sb.AppendLine($"Expected path templates for entity '{entityName}':");
        if (!string.IsNullOrWhiteSpace(interfaceDir))
        {
            sb.AppendLine($"- {interfaceDir}/I{entityName}Repository.cs");
        }
        if (!string.IsNullOrWhiteSpace(repositoryDir))
        {
            sb.AppendLine($"- {repositoryDir}/{entityName}Repository.cs");
        }
        if (!string.IsNullOrWhiteSpace(entitiesDir))
        {
            sb.AppendLine($"- {entitiesDir}/{entityName}.cs");
        }
        if (!string.IsNullOrWhiteSpace(indexesDir))
        {
            sb.AppendLine($"- {indexesDir}/{entityName}Index.cs");
        }
        if (!string.IsNullOrWhiteSpace(controllersDir))
        {
            sb.AppendLine($"- {controllersDir}/{entityName}Controller.cs");
        }
        if (!string.IsNullOrWhiteSpace(testsDir))
        {
            sb.AppendLine($"- {testsDir}/{entityName}RepositoryTests.cs");
        }
        sb.AppendLine("Use these exact roots. Do not create alternative roots with similar names.");
    }

    private static void AppendFrontendExpectedPaths(StringBuilder sb, string repoPath, string entityName)
    {
        FrontendLayout? frontend = DiscoverFrontendLayout(repoPath);
        if (frontend is null)
        {
            return;
        }

        string featureName = entityName.ToLowerInvariant();
        sb.AppendLine();
        sb.AppendLine(
            $"Expected frontend paths for '{entityName}' (under host project '{frontend.WebProjectRoot}', mirror feature '{frontend.ExemplarModuleName}'):");
        string exemplarAbsolute = Path.Combine(
            repoPath,
            frontend.ModulesRoot.Replace('/', Path.DirectorySeparatorChar),
            frontend.ExemplarModuleName);
        if (Directory.Exists(exemplarAbsolute))
        {
            foreach (string subfolder in Directory.EnumerateDirectories(exemplarAbsolute)
                         .Select(Path.GetFileName)
                         .Where(name => !string.IsNullOrWhiteSpace(name))
                         .OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine($"- {frontend.ModulesRoot}/{featureName}/{subfolder}/");
            }
        }
        else
        {
            sb.AppendLine($"- {frontend.ModulesRoot}/{featureName}/<mirror exemplar subfolders>/");
        }

        sb.AppendLine(
            $"- Wire routes/bootstrap in the existing host project (see exemplar under {frontend.ModulesRoot}/{frontend.ExemplarModuleName}/)");
        foreach (string forbidden in frontend.ForbiddenRoots)
        {
            sb.AppendLine($"- Forbidden parallel root: {forbidden}/");
        }

        sb.AppendLine("- Never place .html, services, or controllers directly in the feature folder root.");
    }

    private static void AppendFrontendModuleStructure(StringBuilder sb, string repoPath)
    {
        FrontendLayout? frontend = DiscoverFrontendLayout(repoPath);
        if (frontend is null)
        {
            return;
        }

        string exemplarAbsolute = Path.Combine(
            repoPath,
            frontend.ModulesRoot.Replace('/', Path.DirectorySeparatorChar),
            frontend.ExemplarModuleName);
        if (!Directory.Exists(exemplarAbsolute))
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine($"Frontend feature layout (mirror '{frontend.ExemplarModuleName}' exactly):");
        AppendFeatureDirectoryTree(sb, repoPath, Path.Combine(frontend.ModulesRoot, frontend.ExemplarModuleName), exemplarAbsolute, "  ");
        sb.AppendLine(
            "New features must use the same subfolders. Only bootstrap/router files listed above may sit at feature root.");
    }

    private static void AppendFeatureDirectoryTree(
        StringBuilder sb,
        string repoPath,
        string relativeDir,
        string absoluteDir,
        string indent)
    {
        foreach (string file in Directory.EnumerateFiles(absoluteDir).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"{indent}{Path.GetRelativePath(repoPath, file).Replace('\\', '/')}");
        }

        foreach (string subdir in Directory.EnumerateDirectories(absoluteDir).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            string name = Path.GetFileName(subdir) ?? string.Empty;
            sb.AppendLine($"{indent}{relativeDir.Replace('\\', '/')}/{name}/");
            AppendFeatureDirectoryTree(
                sb,
                repoPath,
                $"{relativeDir}/{name}",
                subdir,
                indent + "  ");
        }
    }

    internal static string NormalizeFeatureModuleRelativePath(
        string relativePath,
        FrontendLayout layout,
        string content)
    {
        string normalized = relativePath.Replace('\\', '/').TrimStart('/');
        if (!normalized.StartsWith(layout.ModulesRoot + "/", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        string remainder = normalized[(layout.ModulesRoot.Length + 1)..];
        int slash = remainder.IndexOf('/');
        if (slash < 0)
        {
            return normalized;
        }

        string feature = remainder[..slash];
        string tail = remainder[(slash + 1)..];
        if (string.IsNullOrWhiteSpace(tail) || tail.Contains('/'))
        {
            return normalized;
        }

        if (layout.AllowedRootFileNames.Any(name => name.Equals(tail, StringComparison.OrdinalIgnoreCase)))
        {
            return normalized;
        }

        string? subfolder = ClassifyFrontendFeatureFile(tail, content, layout);
        return string.IsNullOrWhiteSpace(subfolder)
            ? normalized
            : $"{layout.ModulesRoot}/{feature}/{subfolder}/{tail}";
    }

    internal static bool TryValidateFeatureModulePath(
        string relativePath,
        FrontendLayout layout,
        out string reason)
    {
        reason = string.Empty;
        string normalized = relativePath.Replace('\\', '/').TrimStart('/');
        if (!normalized.StartsWith(layout.ModulesRoot + "/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string remainder = normalized[(layout.ModulesRoot.Length + 1)..];
        int slash = remainder.IndexOf('/');
        if (slash < 0)
        {
            return true;
        }

        string tail = remainder[(slash + 1)..];
        if (string.IsNullOrWhiteSpace(tail) || tail.Contains('/'))
        {
            return true;
        }

        if (layout.AllowedRootFileNames.Any(name => name.Equals(tail, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        reason =
            $"Frontend file '{relativePath}' must be under {layout.ModulesRoot}/<feature>/{string.Join("|", layout.RequiredSubfolders)}/ "
            + $"(only {string.Join(", ", layout.AllowedRootFileNames)} may sit at feature root).";
        return false;
    }

    private static string? ClassifyFrontendFeatureFile(string fileName, string content, FrontendLayout layout)
    {
        if (fileName.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            return PickSubfolder(layout, "views");
        }

        if (fileName.Contains("Proxy", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("Service", StringComparison.OrdinalIgnoreCase)
            || content.Contains(".factory(", StringComparison.Ordinal)
            || content.Contains(".service(", StringComparison.Ordinal))
        {
            return PickSubfolder(layout, "services");
        }

        if (fileName.Contains("Controller", StringComparison.OrdinalIgnoreCase)
            || content.Contains(".controller(", StringComparison.Ordinal))
        {
            return PickSubfolder(layout, "controllers");
        }

        if (fileName.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".ts", StringComparison.OrdinalIgnoreCase))
        {
            return PickSubfolder(layout, "controllers");
        }

        return null;
    }

    private static string? PickSubfolder(FrontendLayout layout, string preferred)
    {
        if (layout.RequiredSubfolders.Any(folder => folder.Equals(preferred, StringComparison.OrdinalIgnoreCase)))
        {
            return preferred;
        }

        return layout.RequiredSubfolders.FirstOrDefault();
    }

    internal static FrontendLayout? DiscoverFrontendLayout(string repoPath)
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

            if (IsSolutionSiblingApplicationFolder(repoPath, relative))
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

        var requiredSubfolders = Directory.EnumerateDirectories(modulesAbsolute)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToList();

        var allowedRootFiles = Directory.EnumerateFiles(modulesAbsolute)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToList();

        return new FrontendLayout(
            best.ModulesRoot,
            webProjectRoot,
            exemplarModule,
            forbidden,
            requiredSubfolders,
            allowedRootFiles);
    }

    internal static string? RemapForbiddenFrontendPath(string relativePath, FrontendLayout layout)
    {
        string normalized = relativePath.Replace('\\', '/').TrimStart('/');
        foreach (string forbidden in layout.ForbiddenRoots)
        {
            if (!normalized.StartsWith(forbidden + "/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string suffix = normalized[forbidden.Length..].TrimStart('/');
            if (suffix.StartsWith("Application/modules/", StringComparison.OrdinalIgnoreCase))
            {
                suffix = suffix["Application/modules/".Length..];
            }
            else if (suffix.StartsWith("modules/", StringComparison.OrdinalIgnoreCase))
            {
                suffix = suffix["modules/".Length..];
            }

            return $"{layout.ModulesRoot}/{suffix}";
        }

        return null;
    }

    private static readonly string[] FeatureModuleSubfolderNames =
    [
        "controllers", "views", "services", "components", "pages", "hooks", "modules", "routes"
    ];

    private static bool HasFeatureModuleChildren(string modulesDir) =>
        Directory.EnumerateDirectories(modulesDir)
            .Any(moduleDir => FeatureModuleSubfolderNames.Any(name =>
                Directory.Exists(Path.Combine(moduleDir, name))));

    private static bool IsSolutionSiblingApplicationFolder(string repoPath, string modulesRelativePath)
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
                {
                    string name = Path.GetFileName(path);
                    return !name.Contains("WebAPI", StringComparison.OrdinalIgnoreCase)
                           && !name.Contains("Repository", StringComparison.OrdinalIgnoreCase)
                           && !name.Contains("UnitTest", StringComparison.OrdinalIgnoreCase)
                           && !name.Contains("Test", StringComparison.OrdinalIgnoreCase)
                           && !name.Contains(".Db", StringComparison.OrdinalIgnoreCase);
                });
            if (!string.IsNullOrWhiteSpace(csproj))
            {
                return Path.GetRelativePath(repoPath, current).Replace('\\', '/');
            }

            current = Path.GetDirectoryName(current);
        }

        return null;
    }

    private static void AppendRepositoryImplementationConventions(StringBuilder sb, string repoPath)
    {
        var implementationFiles = Directory.GetFiles(repoPath, "*Repository.cs", SearchOption.AllDirectories)
            .Where(path => !Path.GetFileName(path).StartsWith("I", StringComparison.OrdinalIgnoreCase))
            .Where(path => !Path.GetFileName(path).Equals("Repository.cs", StringComparison.OrdinalIgnoreCase))
            .Take(4)
            .ToList();
        if (implementationFiles.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine("Repository implementation conventions:");
        foreach (var file in implementationFiles)
        {
            string relative = Path.GetRelativePath(repoPath, file).Replace('\\', '/');
            sb.AppendLine($"- Implementation: {relative}");
            sb.AppendLine($"  Class signature: {ExtractClassSignature(file)}");
            sb.AppendLine($"  Constructor signature: {ExtractConstructorSignature(file)}");
        }
    }

    private static void AppendRepositoryUsingConventions(StringBuilder sb, string repoPath)
    {
        var implementationFiles = Directory.GetFiles(repoPath, "*Repository.cs", SearchOption.AllDirectories)
            .Where(path => !Path.GetFileName(path).StartsWith("I", StringComparison.OrdinalIgnoreCase))
            .Where(path => !Path.GetFileName(path).Equals("Repository.cs", StringComparison.OrdinalIgnoreCase))
            .Take(6)
            .ToList();
        if (implementationFiles.Count == 0)
        {
            return;
        }

        var commonUsings = implementationFiles
            .SelectMany(file => File.ReadLines(file).Take(20))
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("using ", StringComparison.Ordinal) && line.EndsWith(";", StringComparison.Ordinal))
            .GroupBy(line => line, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .Take(10)
            .Select(group => group.Key)
            .ToList();

        if (commonUsings.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine("Common using directives in repository implementations:");
        foreach (var directive in commonUsings)
        {
            sb.AppendLine($"- {directive}");
        }
    }

    private static void AppendCriticalRepositoryContract(StringBuilder sb, string repoPath, string taskPrompt, string entityName)
    {
        var implementationFiles = Directory.GetFiles(repoPath, "*Repository.cs", SearchOption.AllDirectories)
            .Where(path => !Path.GetFileName(path).StartsWith("I", StringComparison.OrdinalIgnoreCase))
            .Where(path => !Path.GetFileName(path).Equals("Repository.cs", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                        && !path.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (implementationFiles.Count == 0)
        {
            return;
        }

        var signals = ExtractTaskSignals(taskPrompt);
        string? exemplar = implementationFiles
            .OrderByDescending(path => ScoreFileCore(Path.GetRelativePath(repoPath, path).Replace('\\', '/'), signals))
            .ThenBy(path => path.Length)
            .FirstOrDefault();
        exemplar ??= implementationFiles.OrderBy(path => path.Length).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(exemplar))
        {
            return;
        }

        string exemplarClass = ExtractClassSignature(exemplar);
        string exemplarCtor = ExtractConstructorSignature(exemplar);
        string exemplarEntity = Path.GetFileNameWithoutExtension(exemplar);
        if (exemplarEntity.EndsWith("Repository", StringComparison.OrdinalIgnoreCase))
        {
            exemplarEntity = exemplarEntity[..^"Repository".Length];
        }

        string? exemplarInterfaceFile = Directory.GetFiles(repoPath, $"I{exemplarEntity}Repository.cs", SearchOption.AllDirectories)
            .FirstOrDefault(path => path.Contains("Interfaces", StringComparison.OrdinalIgnoreCase));
        string exemplarInterface = string.IsNullOrWhiteSpace(exemplarInterfaceFile)
            ? $"public interface I{exemplarEntity}Repository: IRepository<{exemplarEntity}>"
            : ExtractInterfaceSignature(exemplarInterfaceFile);

        sb.AppendLine();
        sb.AppendLine("Critical repository contract (MUST follow exactly):");
        sb.AppendLine($"- Exemplar interface from codebase: {exemplarInterface}");
        sb.AppendLine($"- Exemplar implementation from codebase: {exemplarClass}");
        sb.AppendLine($"- Exemplar constructor from codebase: {exemplarCtor}");
        sb.AppendLine($"- Required new interface: public interface I{entityName}Repository: IRepository<{entityName}>");
        sb.AppendLine($"- Required new implementation: public class {entityName}Repository: Repository<{entityName}>, I{entityName}Repository");
        sb.AppendLine($"- Required new constructor: public {entityName}Repository(IDbStore dbStore) : base(dbStore)");
        sb.AppendLine("- Never generate repository class with only ': I{Entity}Repository' and missing Repository<{Entity}> base.");
    }

    private static void AppendInheritedLayerMembers(StringBuilder sb, string repoPath, string roleName)
    {
        string? exemplarPath = Directory
            .GetFiles(repoPath, $"*{roleName}.cs", SearchOption.AllDirectories)
            .Where(path => !Path.GetFileName(path).StartsWith('I')
                        && !Path.GetFileName(path).Equals($"{roleName}.cs", StringComparison.OrdinalIgnoreCase)
                        && !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path.Length)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(exemplarPath))
        {
            return;
        }

        string content = File.ReadAllText(exemplarPath);
        string? memberContext = ClassMemberAccessGuard.BuildBaseTypeContext(repoPath, content);
        if (string.IsNullOrWhiteSpace(memberContext))
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine($"{roleName} layer — {memberContext}");
    }

    private static void AppendControllerPatterns(StringBuilder sb, string repoPath)
    {
        string? exemplar = Directory
            .GetFiles(repoPath, "*Controller.cs", SearchOption.AllDirectories)
            .Where(path => !Path.GetFileName(path).Equals("Controller.cs", StringComparison.OrdinalIgnoreCase)
                        && !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(path => File.ReadAllText(path).Contains(".Insert(", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(exemplar))
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine("Controller conventions:");
        sb.AppendLine("- Inject only I{Entity}Repository for the controller entity (do not copy unrelated repositories from other controllers).");
        sb.AppendLine("- Use IRepository<T> members (Insert, GetById, Count, Query) — never invent methods like Insert{Entity}.");
        sb.AppendLine($"- Persistence exemplar: {Path.GetRelativePath(repoPath, exemplar).Replace('\\', '/')}");
        foreach (string line in File.ReadAllLines(exemplar).Where(l => l.Contains("Insert(", StringComparison.Ordinal) || l.Contains("Repository(", StringComparison.Ordinal)).Take(4))
        {
            sb.AppendLine($"  {line.Trim()}");
        }
    }

    private static void AppendRepositoryQueryPatterns(StringBuilder sb, string repoPath)
    {
        string? dbStorePath = Directory
            .EnumerateFiles(repoPath, "IDbStore.cs", SearchOption.AllDirectories)
            .FirstOrDefault(path => !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(dbStorePath))
        {
            string? signature = File.ReadAllLines(dbStorePath)
                .Select(line => line.Trim())
                .FirstOrDefault(line => line.Contains("Query<", StringComparison.Ordinal) && line.Contains('('));
            if (!string.IsNullOrWhiteSpace(signature))
            {
                sb.AppendLine($"- IDbStore query signature: {signature}");
            }
        }

        string? baseRepoPath = Directory
            .EnumerateFiles(repoPath, "Repository.cs", SearchOption.AllDirectories)
            .FirstOrDefault(path => Path.GetFileName(path).Equals("Repository.cs", StringComparison.OrdinalIgnoreCase)
                                 && path.Contains("Repository", StringComparison.OrdinalIgnoreCase)
                                 && !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(baseRepoPath))
        {
            foreach (string line in File.ReadAllLines(baseRepoPath))
            {
                string trimmed = line.Trim();
                if (trimmed.Contains("Query(", StringComparison.Ordinal) || trimmed.Contains("SingleSearch(", StringComparison.Ordinal))
                {
                    sb.AppendLine($"- Base repository API: {trimmed}");
                }
            }
        }

        string? exemplar = Directory
            .GetFiles(repoPath, "*Repository.cs", SearchOption.AllDirectories)
            .Where(path => !Path.GetFileName(path).StartsWith('I')
                        && !Path.GetFileName(path).Equals("Repository.cs", StringComparison.OrdinalIgnoreCase)
                        && !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(path => File.ReadAllText(path).Contains("typeof(", StringComparison.Ordinal)
                                   && File.ReadAllText(path).Contains(".Name", StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(exemplar))
        {
            sb.AppendLine($"- Index name usage exemplar: {Path.GetRelativePath(repoPath, exemplar).Replace('\\', '/')}");
            sb.AppendLine("  Pass index name string (e.g. typeof({Entity}Index).Name) to Query/SingleSearch — never DbStore.Query<T>() without indexName.");
        }
    }

    private static string? FindDirectory(string repoPath, string token, string? excludeToken = null)
    {
        var matches = Directory.EnumerateDirectories(repoPath, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(repoPath, path).Replace('\\', '/'))
            .Where(relative => relative.Contains(token, StringComparison.OrdinalIgnoreCase))
            .Where(relative => string.IsNullOrWhiteSpace(excludeToken) || !relative.Contains(excludeToken, StringComparison.OrdinalIgnoreCase))
            .OrderBy(relative => relative.Length)
            .ToList();
        return matches.FirstOrDefault();
    }

    private static string? FindWebApiControllersDir(string repoPath)
    {
        return Directory.EnumerateDirectories(repoPath, "Controllers", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(repoPath, path).Replace('\\', '/'))
            .Where(relative => relative.Contains("WebAPI", StringComparison.OrdinalIgnoreCase))
            .OrderBy(relative => relative.Length)
            .FirstOrDefault()
            ?? FindDirectory(repoPath, "Controllers");
    }

    private static string? FindRepositoryInterfacesDir(string repoPath)
    {
        return Directory.EnumerateDirectories(repoPath, "Interfaces", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(repoPath, path).Replace('\\', '/'))
            .Where(relative => relative.Contains("Repository", StringComparison.OrdinalIgnoreCase))
            .OrderBy(relative => relative.Length)
            .FirstOrDefault()
            ?? FindDirectory(repoPath, "Interfaces");
    }

    private static string? FindRepositoryEntitiesDir(string repoPath)
    {
        return Directory.EnumerateDirectories(repoPath, "Entities", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(repoPath, path).Replace('\\', '/'))
            .Where(relative => relative.Contains("Repository", StringComparison.OrdinalIgnoreCase))
            .OrderBy(relative => relative.Length)
            .FirstOrDefault()
            ?? FindDirectory(repoPath, "Entities");
    }

    private static string? FindRepositoryIndexesDir(string repoPath)
    {
        var preferred = Directory.EnumerateDirectories(repoPath, "Indexes", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(repoPath, path).Replace('\\', '/'))
            .Where(relative => relative.Contains("Repository", StringComparison.OrdinalIgnoreCase))
            .OrderBy(relative => relative.Length)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            return preferred;
        }

        return Directory.EnumerateDirectories(repoPath, "Index", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(repoPath, path).Replace('\\', '/'))
            .Where(relative => relative.Contains("Repository", StringComparison.OrdinalIgnoreCase))
            .OrderBy(relative => relative.Length)
            .FirstOrDefault()
            ?? FindDirectory(repoPath, "Indexes");
    }

    private static string? FindRepositoryImplementationDir(string repoPath)
    {
        var candidate = Directory.EnumerateFiles(repoPath, "*Repository.cs", SearchOption.AllDirectories)
            .Where(path => !Path.GetFileName(path).StartsWith("I", StringComparison.OrdinalIgnoreCase))
            .Where(path => !Path.GetFileName(path).Equals("Repository.cs", StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetRelativePath(repoPath, Path.GetDirectoryName(path) ?? string.Empty).Replace('\\', '/'))
            .Where(relative => relative.Contains("Repository", StringComparison.OrdinalIgnoreCase))
            .OrderBy(relative => relative.Length)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            return candidate;
        }

        return FindDirectory(repoPath, "Repository", excludeToken: "Interfaces");
    }

    private static void AppendCategory(StringBuilder sb, string title, IEnumerable<string> paths, Func<string, bool> predicate, string repoPath)
    {
        var selected = paths.Where(predicate).Take(3).ToList();
        if (selected.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine($"{title}:");
        foreach (var path in selected)
        {
            string relative = Path.GetRelativePath(repoPath, path).Replace('\\', '/');
            sb.AppendLine($"- {relative}");
            sb.AppendLine(ExtractSnippet(path));
        }
    }

    private static string ExtractSnippet(string path)
    {
        try
        {
            int maxLines = 50;
            int maxChars = 1200;
            var lines = File.ReadLines(path).Take(maxLines).ToArray();
            var snippet = string.Join('\n', lines);
            if (snippet.Length > maxChars)
            {
                snippet = snippet[..maxChars];
            }

            return $"  Snippet:\n{Indent(snippet, "    ")}";
        }
        catch
        {
            return "  Snippet: <unavailable>";
        }
    }

    private static string ExtractClassSignature(string filePath)
    {
        foreach (var line in File.ReadLines(filePath))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("public class ", StringComparison.Ordinal))
            {
                return trimmed;
            }
        }

        return "public class ExampleRepository: Repository<Example>, IExampleRepository";
    }

    private static string ExtractConstructorSignature(string filePath)
    {
        string className = Path.GetFileNameWithoutExtension(filePath);
        foreach (var line in File.ReadLines(filePath))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith($"public {className}(", StringComparison.Ordinal))
            {
                return trimmed;
            }
        }

        return $"public {className}(IDbStore dbStore) : base(dbStore)";
    }

    private static string Indent(string text, string prefix)
    {
        return string.Join('\n', text.Split('\n').Select(line => $"{prefix}{line}"));
    }

    private static List<string> ExtractTaskSignals(string taskPrompt)
    {
        var tokens = taskPrompt
            .Split(new[] { ' ', '\t', '\r', '\n', ',', '.', ':', ';', '-', '_', '/', '\\', '(', ')', '[', ']', '{', '}', '\'' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length >= 4)
            .Select(token => token.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!tokens.Any(t => t.Equals("Employee", StringComparison.OrdinalIgnoreCase)))
        {
            tokens.Add("Employee");
        }

        return tokens;
    }

    private static string? InferTargetEntityName(string taskPrompt)
    {
        var quoted = Regex.Matches(taskPrompt, @"'([A-Za-z][A-Za-z0-9_]*)'");
        foreach (Match match in quoted)
        {
            if (match.Groups.Count > 1)
            {
                return match.Groups[1].Value;
            }
        }

        var entityPattern = Regex.Match(taskPrompt, @"entity\s+called\s+([A-Za-z][A-Za-z0-9_]*)", RegexOptions.IgnoreCase);
        if (entityPattern.Success && entityPattern.Groups.Count > 1)
        {
            return entityPattern.Groups[1].Value;
        }

        return null;
    }

    private static int ScoreFile(string relativePath, IReadOnlyList<string> signals, FrontendLayout? frontend)
    {
        string normalizedPath = relativePath.Replace('\\', '/');
        int score = ScoreFileCore(relativePath, signals);

        if (frontend is null)
        {
            return score;
        }

        if (normalizedPath.StartsWith(frontend.ModulesRoot + "/", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(frontend.WebProjectRoot + "/", StringComparison.OrdinalIgnoreCase))
        {
            score += 40;
        }

        if (frontend.ForbiddenRoots.Any(root => normalizedPath.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase)))
        {
            score -= 80;
        }

        return score;
    }

    private static int ScoreFileCore(string path, IReadOnlyList<string> signals)
    {
        string fileName = Path.GetFileNameWithoutExtension(path);
        string normalizedPath = path.Replace('\\', '/');
        int score = 0;

        foreach (var signal in signals)
        {
            if (fileName.Contains(signal, StringComparison.OrdinalIgnoreCase))
            {
                score += 20;
            }
            if (normalizedPath.Contains(signal, StringComparison.OrdinalIgnoreCase))
            {
                score += 8;
            }
        }

        if (normalizedPath.Contains("Controller", StringComparison.OrdinalIgnoreCase)) score += 8;
        if (normalizedPath.Contains("Repository", StringComparison.OrdinalIgnoreCase)) score += 8;
        if (normalizedPath.Contains("Entities", StringComparison.OrdinalIgnoreCase)) score += 8;
        if (normalizedPath.Contains("Index", StringComparison.OrdinalIgnoreCase)) score += 6;
        if (normalizedPath.Contains("UnitTest", StringComparison.OrdinalIgnoreCase) || normalizedPath.Contains("Tests", StringComparison.OrdinalIgnoreCase)) score += 10;
        if (normalizedPath.Contains("/controllers/", StringComparison.OrdinalIgnoreCase)) score += 8;
        if (normalizedPath.Contains("/services/", StringComparison.OrdinalIgnoreCase)) score += 8;
        if (normalizedPath.Contains("/views/", StringComparison.OrdinalIgnoreCase)) score += 8;

        return score;
    }

    private static string ClampContext(string content, int maxChars)
    {
        if (content.Length <= maxChars)
        {
            return content;
        }

        return content[..maxChars] + "\n\n[Context truncated to keep prompt focused.]";
    }
}

internal sealed record FrontendLayout(
    string ModulesRoot,
    string WebProjectRoot,
    string ExemplarModuleName,
    IReadOnlyList<string> ForbiddenRoots,
    IReadOnlyList<string> RequiredSubfolders,
    IReadOnlyList<string> AllowedRootFileNames);

readonly record struct RagContextBundle(
    string StructureContext,
    string LegacyImplementationContext,
    string CombinedContext);
