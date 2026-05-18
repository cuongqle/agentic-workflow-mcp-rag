using System.Text;
using System.Text.RegularExpressions;

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
        content.AppendLine($"- Frontend controller roots: {FormatList(FindDirectoriesContaining(repoPath, "controllers"))}");
        content.AppendLine($"- Frontend service roots: {FormatList(FindDirectoriesContaining(repoPath, "services"))}");
        content.AppendLine($"- Frontend view roots: {FormatList(FindDirectoriesContaining(repoPath, "views"))}");

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

        AppendCorpusSummary(sb, candidateFiles, repoPath);
        AppendSemanticContext(sb, ragIndex, taskPrompt);

        var ranked = candidateFiles
            .Select(path => new
            {
                Path = path,
                Score = ScoreFile(path, signals)
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
        AppendCategory(sb, "Frontend controllers/services/views", ranked, path =>
            path.Contains("controllers", StringComparison.OrdinalIgnoreCase)
            || path.Contains("services", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".html", StringComparison.OrdinalIgnoreCase), repoPath);
        AppendCategory(sb, "Unit tests", ranked, path =>
            path.Contains("UnitTest", StringComparison.OrdinalIgnoreCase)
            || path.Contains("Tests", StringComparison.OrdinalIgnoreCase), repoPath);

        AppendRepositoryConventions(sb, repoPath);
        AppendRepositoryImplementationConventions(sb, repoPath);
        AppendRepositoryUsingConventions(sb, repoPath);
        AppendCriticalRepositoryContract(sb, repoPath, taskPrompt, entityName);
        AppendExpectedPaths(sb, repoPath, entityName);

        sb.AppendLine("Required generated file set for new entity:");
        sb.AppendLine("- WebAPI controller");
        sb.AppendLine("- Repository interface (e.g. I{Entity}Repository)");
        sb.AppendLine("- Repository implementation (e.g. {Entity}Repository)");
        sb.AppendLine("- Repository entity model");
        sb.AppendLine("- Repository index");
        sb.AppendLine("- Frontend controller/service/view");
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
            .OrderByDescending(path => ScoreFile(path, signals))
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
            var lines = File.ReadLines(path).Take(30).ToArray();
            var snippet = string.Join('\n', lines);
            if (snippet.Length > 450)
            {
                snippet = snippet[..450];
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

    private static int ScoreFile(string path, IReadOnlyList<string> signals)
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

readonly record struct RagContextBundle(
    string StructureContext,
    string LegacyImplementationContext,
    string CombinedContext);
