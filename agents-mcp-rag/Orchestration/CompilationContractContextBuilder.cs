using System.Text.RegularExpressions;
using agents_mcp_rag.Infrastructure;

static class CompilationContractContextBuilder
{
    public static string Build(
        string repoPath,
        IReadOnlyList<string> allowedFiles,
        IReadOnlyList<AgentFinding>? buildFindings = null)
    {
        var contextLines = new List<string>
        {
            "Use only these declared contracts and signatures."
        };

        AppendRepositoryTestExemplarContext(repoPath, allowedFiles, buildFindings, contextLines);

        string exemplarContext = CodeExemplarContext.BuildForCompilationFix(repoPath, allowedFiles);
        if (!string.IsNullOrWhiteSpace(exemplarContext))
        {
            contextLines.Add(exemplarContext);
        }

        string? wiringContext = DependencyWiringAuditor.BuildRegistrationContext(repoPath);
        if (!string.IsNullOrWhiteSpace(wiringContext))
        {
            contextLines.Add(wiringContext);
        }

        foreach (var relative in allowedFiles.Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)).Take(24))
        {
            string absolute = Path.Combine(repoPath, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(absolute))
            {
                continue;
            }

            string content = File.ReadAllText(absolute);
            contextLines.Add($"File: {relative}");
            foreach (Match match in Regex.Matches(content, @"public\s+(?:interface|class)\s+[^{\r\n]+"))
            {
                contextLines.Add($"- {match.Value.Trim()}");
            }
            foreach (Match match in Regex.Matches(content, @"public\s+[A-Za-z0-9_<>\[\],\s\?]+\s+[A-Za-z_][A-Za-z0-9_]*\s*\([^)]*\)"))
            {
                string signature = Regex.Replace(match.Value.Trim(), @"\s+", " ");
                contextLines.Add($"- {signature}");
            }
            foreach (Match match in Regex.Matches(content, @"public\s+[A-Za-z0-9_<>\[\],\s\?]+\s+[A-Za-z_][A-Za-z0-9_]*\s*\{\s*get;\s*(set;)?\s*\}"))
            {
                string property = Regex.Replace(match.Value.Trim(), @"\s+", " ");
                contextLines.Add($"- {property}");
            }
        }

        if (contextLines.Count == 1)
        {
            return "- No explicit contract declarations were collected.";
        }

        string joined = string.Join('\n', contextLines);
        return joined.Length > 6000 ? joined[..6000] + "\n[contract context truncated]" : joined;
    }

    private static void AppendRepositoryTestExemplarContext(
        string repoPath,
        IReadOnlyList<string> allowedFiles,
        IReadOnlyList<AgentFinding>? buildFindings,
        List<string> contextLines)
    {
        bool hasTestTargets = allowedFiles.Any(BuildFailureClassifier.IsTestArtifactPath)
            || (buildFindings is not null && buildFindings.Any(f => BuildFailureClassifier.ClassifyMessage(f.Message) == BuildFailureScope.Test));
        if (!hasTestTargets)
        {
            return;
        }

        string? testsDir = TestCoverageAuditor.GetRepositoryTestsDirectory(repoPath);
        if (string.IsNullOrWhiteSpace(testsDir))
        {
            return;
        }

        string testsAbsoluteDir = Path.Combine(repoPath, testsDir.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(testsAbsoluteDir))
        {
            return;
        }

        string? exemplarPath = Directory
            .EnumerateFiles(testsAbsoluteDir, "*RepositoryTests.cs", SearchOption.TopDirectoryOnly)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(exemplarPath))
        {
            return;
        }

        string exemplarRelative = Path.GetRelativePath(repoPath, exemplarPath).Replace('\\', '/');
        string exemplarContent = File.ReadAllText(exemplarPath);
        if (exemplarContent.Length > 2500)
        {
            exemplarContent = exemplarContent[..2500] + "\n// [exemplar truncated]";
        }

        contextLines.Add("Repository test exemplar (mirror structure exactly; valid C# only):");
        contextLines.Add($"File: {exemplarRelative}");
        contextLines.Add(exemplarContent);
    }
}
