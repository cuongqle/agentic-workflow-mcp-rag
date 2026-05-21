using System.Text;
using System.Text.RegularExpressions;
using agents_mcp_rag.Infrastructure;

static class RagContextComposer
{
    public static Task<RagContextBundle> BuildAsync(
        string repoPath,
        string taskPrompt,
        CodebaseRagIndex ragIndex,
        RepoContract contract)
    {
        string structureContext = ClampContext(contract.FormatStructureSummary(), 4_000);
        string legacyContext = ClampContext(BuildLegacyImplementationContext(repoPath, taskPrompt, ragIndex, contract), 10_000);

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

    private static string BuildLegacyImplementationContext(
        string repoPath,
        string taskPrompt,
        CodebaseRagIndex ragIndex,
        RepoContract contract)
    {
        if (!Directory.Exists(repoPath))
        {
            return "Legacy implementation context unavailable: repository path not found.";
        }

        var sb = new StringBuilder();
        sb.AppendLine("Legacy implementation exemplars (use these conventions):");
        sb.AppendLine(contract.FormatAgentPreamble(InferTargetEntityName(taskPrompt) ?? "NewEntity"));
        sb.AppendLine();

        var signals = ExtractTaskSignals(taskPrompt);
        string entityName = InferTargetEntityName(taskPrompt) ?? "NewEntity";
        var candidateFiles = RepoCodeFileScanner.EnumerateRelevantFiles(repoPath).ToList();
        FrontendModuleTemplate? frontend = contract.Frontend;

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
        AppendCategory(sb, "Frontend UI modules", ranked, path => IsFrontendModulePath(path, contract), repoPath);
        AppendCategory(sb, "Unit tests", ranked, path =>
            path.Contains("UnitTest", StringComparison.OrdinalIgnoreCase)
            || path.Contains("Tests", StringComparison.OrdinalIgnoreCase), repoPath);

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

        return sb.ToString();
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

    private static bool IsFrontendModulePath(string path, RepoContract contract)
    {
        string normalized = path.Replace('\\', '/');
        if (normalized.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".vue", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".ts", StringComparison.OrdinalIgnoreCase))
        {
            if (contract.Frontend is not null
                && normalized.StartsWith(contract.Frontend.ModulesRoot + "/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (contract.Frontend is null)
        {
            return normalized.Contains("/controllers/", StringComparison.OrdinalIgnoreCase)
                   || normalized.Contains("/services/", StringComparison.OrdinalIgnoreCase)
                   || normalized.Contains("/views/", StringComparison.OrdinalIgnoreCase)
                   || normalized.Contains("/proxies/", StringComparison.OrdinalIgnoreCase);
        }

        if (!normalized.StartsWith(contract.Frontend.ModulesRoot + "/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return contract.Frontend.RequiredSubfolders.Any(name =>
                   normalized.Contains($"/{name}/", StringComparison.OrdinalIgnoreCase))
               || contract.Frontend.AllowedRootFileNames.Any(name =>
                   normalized.EndsWith("/" + name, StringComparison.OrdinalIgnoreCase));
    }

    private static int ScoreFile(string relativePath, IReadOnlyList<string> signals, FrontendModuleTemplate? frontend)
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

        if (frontend.LayoutMode == FrontendLayoutMode.HostModulePages)
        {
            string hostPrefix = $"{frontend.ModulesRoot}/{frontend.ExemplarModuleName}/";
            if (normalizedPath.StartsWith(hostPrefix, StringComparison.OrdinalIgnoreCase))
            {
                score += 30;
            }
            else if (normalizedPath.StartsWith(frontend.ModulesRoot + "/", StringComparison.OrdinalIgnoreCase))
            {
                score -= 50;
            }
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

    internal static string? DetectCanonicalDirectoryForFileSuffix(
        string repoPath,
        string fileSuffix,
        string? preferredDirectoryName = null) =>
        RepoContractDiscoverer.DetectCanonicalDirectoryForFileSuffix(repoPath, fileSuffix, preferredDirectoryName);
}

readonly record struct RagContextBundle(
    string StructureContext,
    string LegacyImplementationContext,
    string CombinedContext);
