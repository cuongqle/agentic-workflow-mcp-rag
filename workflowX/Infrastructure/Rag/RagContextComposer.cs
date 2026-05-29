using System.Text;
using System.Text.RegularExpressions;

namespace workflowX.Infrastructure;

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
        AppendImplementationRules(sb, contract);
        sb.AppendLine();

        var candidateFiles = RepoCodeFileScanner.EnumerateRelevantFiles(repoPath, contract)
            .OrderBy(path => Path.GetRelativePath(repoPath, path).Replace('\\', '/'), StringComparer.OrdinalIgnoreCase)
            .ToList();

        AppendCorpusSummary(sb, candidateFiles, repoPath);
        AppendSemanticContext(sb, ragIndex, taskPrompt, contract);

        RepoStack stack = contract.Stack;
        stack.WhenDotNet(() => CSharpRagContextSupport.AppendImplementationContext(sb, repoPath, contract));
        stack.WhenFrontend(() =>
            FrontendRagContextSupport.AppendImplementationContext(sb, candidateFiles, contract, repoPath));

        return sb.ToString();
    }

    private static void AppendImplementationRules(StringBuilder sb, RepoContract contract)
    {
        sb.AppendLine();
        sb.AppendLine("Implementation rules (apply + compliance enforce these):");
        sb.AppendLine("- Mirror exemplars from semantic RAG hits and on-disk patterns (naming, inheritance, APIs).");
        sb.AppendLine("- Ship complete code: real method bodies; no stubs, TODO, or NotImplementedException.");
        contract.Stack.WhenDotNet(() => CSharpRagContextSupport.AppendDotNetImplementationRules(sb));
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

    private static void AppendSemanticContext(StringBuilder sb, CodebaseRagIndex ragIndex, string taskPrompt, RepoContract contract)
    {
        var semanticQueries = new List<string>
        {
            taskPrompt,
            "coding style naming patterns syntax conventions repository implementation examples",
            "unit tests integration tests mocking assertions conventions"
        };

        RepoStack stack = contract.Stack;
        semanticQueries.AddRange(stack.WhenDotNet(CSharpRagContextSupport.SemanticQueries()));
        semanticQueries.AddRange(stack.WhenFrontend(FrontendRagContextSupport.SemanticQueries()));

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

    private static string Indent(string text, string prefix) =>
        string.Join('\n', text.Split('\n').Select(line => $"{prefix}{line}"));

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
