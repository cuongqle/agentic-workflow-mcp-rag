using System.Text;

namespace workflowX.Infrastructure.Rag.DotNet;

/// <summary>
/// .NET/C#-specific RAG context sections — only invoked when <see cref="RepoStack.DotNet"/>.
/// </summary>
internal static class CSharpRagContextSupport
{
    internal static void AppendDotNetImplementationRules(StringBuilder sb)
    {
        sb.AppendLine("- .NET backend: new role interfaces need full implementations; entity/index property names must match.");
        sb.AppendLine("- Do not rewrite bootstrap/composition-root files or pre-existing store/base contracts.");
    }

    internal static void AppendImplementationContext(
        StringBuilder sb,
        string repoPath,
        string taskPrompt,
        RepoContract contract,
        IReadOnlyList<string> rankedPaths)
    {
        AppendBackendExemplarCategories(sb, rankedPaths, repoPath);

        CodeExemplarContext.AppendDiscoveredExemplars(sb, repoPath, taskPrompt);

        string? wiringContext = DependencyWiringAuditor.BuildRegistrationContext(repoPath, contract.RegistrationScope);
        if (!string.IsNullOrWhiteSpace(wiringContext))
        {
            sb.AppendLine();
            sb.AppendLine(wiringContext);
        }

        string? bootstrapContext = TestBootstrapContext.BuildContext(repoPath, contract.RegistrationScope);
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

        string? contractRules = InterfaceCallSignatureGuard.BuildRagContext(
            repoPath,
            Array.Empty<GeneratedFile>());
        if (!string.IsNullOrWhiteSpace(contractRules))
        {
            sb.AppendLine();
            sb.AppendLine(contractRules);
        }
    }

    internal static int ScoreBackendPath(string normalizedPath)
    {
        int score = 0;
        if (normalizedPath.Contains("Controller", StringComparison.OrdinalIgnoreCase)) score += 8;
        if (normalizedPath.Contains("Repository", StringComparison.OrdinalIgnoreCase)) score += 8;
        if (normalizedPath.Contains("Entities", StringComparison.OrdinalIgnoreCase)) score += 8;
        if (normalizedPath.Contains("Index", StringComparison.OrdinalIgnoreCase)) score += 6;
        if (normalizedPath.Contains("UnitTest", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Contains("Tests", StringComparison.OrdinalIgnoreCase))
        {
            score += 10;
        }

        return score;
    }

    private static void AppendBackendExemplarCategories(StringBuilder sb, IReadOnlyList<string> rankedPaths, string repoPath)
    {
        AppendCategory(sb, "WebAPI controllers", rankedPaths,
            path => path.Contains("Controller", StringComparison.OrdinalIgnoreCase)
                    && path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase),
            repoPath);
        AppendCategory(sb, "Repository/entities/indexes", rankedPaths, path =>
            path.Contains("Repository", StringComparison.OrdinalIgnoreCase)
            || path.Contains("Entities", StringComparison.OrdinalIgnoreCase)
            || path.Contains("Index", StringComparison.OrdinalIgnoreCase),
            repoPath);
        AppendCategory(sb, "Unit tests", rankedPaths, path =>
            path.Contains("UnitTest", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase),
            repoPath);
    }

    internal static IEnumerable<string> SemanticQueries()
    {
        yield return "controller repository entity index patterns validation and error handling";
        yield return "dependency injection composition root registration bootstrap";
    }

    internal static string? DetectCanonicalDirectoryForFileSuffix(
        string repoPath,
        string fileSuffix,
        string? preferredDirectoryName = null) =>
        DotNetRepoContractDiscoverer.DetectCanonicalDirectoryForFileSuffix(repoPath, fileSuffix, preferredDirectoryName);

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

    private static string Indent(string text, string prefix) =>
        string.Join('\n', text.Split('\n').Select(line => $"{prefix}{line}"));
}
