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

        AppendLayerTestExemplarContext(repoPath, allowedFiles, buildFindings, contextLines);
        AppendEntityIndexPairContext(repoPath, allowedFiles, buildFindings, contextLines);

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

        AppendAuthoritativeInfrastructureContracts(repoPath, contextLines);
        AppendTestBootstrapContext(repoPath, contextLines);

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

    private static void AppendLayerTestExemplarContext(
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

        string? targetTestFile = allowedFiles
            .FirstOrDefault(path => Path.GetFileName(path).EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase));
        string? productionBaseName = targetTestFile is not null
            ? TestCoverageAuditor.ExtractProductionBaseNameFromTestFileName(Path.GetFileName(targetTestFile))
            : null;

        TestConvention? convention = !string.IsNullOrWhiteSpace(productionBaseName)
            ? TestCoverageAuditor.FindConventionForProductionBase(repoPath, productionBaseName)
            : TestCoverageAuditor.DiscoverTestConventions(repoPath).FirstOrDefault();

        if (convention is null)
        {
            return;
        }

        string testsAbsoluteDir = Path.Combine(
            repoPath,
            convention.TestDirectory.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(testsAbsoluteDir))
        {
            return;
        }

        string exemplarGlob = $"*{Path.GetFileNameWithoutExtension(convention.ProductionFileSuffix)}Tests.cs";
        string? exemplarPath = Directory
            .EnumerateFiles(testsAbsoluteDir, exemplarGlob, SearchOption.TopDirectoryOnly)
            .FirstOrDefault();
        exemplarPath ??= Directory
            .EnumerateFiles(testsAbsoluteDir, "*Tests.cs", SearchOption.TopDirectoryOnly)
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

        string layer = Path.GetFileNameWithoutExtension(convention.ProductionFileSuffix);
        contextLines.Add($"{layer} test exemplar (mirror structure exactly; valid C# only):");
        contextLines.Add($"File: {exemplarRelative}");
        contextLines.Add(exemplarContent);
    }

    private static void AppendEntityIndexPairContext(
        string repoPath,
        IReadOnlyList<string> allowedFiles,
        IReadOnlyList<AgentFinding>? buildFindings,
        List<string> contextLines)
    {
        var entityNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var relative in allowedFiles)
        {
            string fileName = Path.GetFileName(relative);
            if (fileName.EndsWith("Index.cs", StringComparison.OrdinalIgnoreCase))
            {
                entityNames.Add(Path.GetFileNameWithoutExtension(fileName).Replace("Index", string.Empty, StringComparison.Ordinal));
            }
        }

        foreach (var finding in buildFindings ?? Array.Empty<AgentFinding>())
        {
            foreach (Match match in Regex.Matches(
                         finding.Message,
                         @"'([A-Za-z_][A-Za-z0-9_]*)'\s+does not contain a definition for\s+'([A-Za-z_][A-Za-z0-9_]*)'",
                         RegexOptions.IgnoreCase))
            {
                entityNames.Add(match.Groups[1].Value);
                contextLines.Add(
                    $"CS1061 contract: type '{match.Groups[1].Value}' must declare member '{match.Groups[2].Value}' (add property or fix index map to existing members only).");
            }
        }

        foreach (string entityName in entityNames.Where(n => n.Length > 1))
        {
            string? entityPath = Directory
                .EnumerateFiles(repoPath, $"{entityName}.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                               && !path.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path.Contains("/Entities/", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(path => path.Length)
                .Select(path => Path.GetRelativePath(repoPath, path).Replace('\\', '/'))
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(entityPath))
            {
                continue;
            }

            string absolute = Path.Combine(repoPath, entityPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(absolute))
            {
                continue;
            }

            string content = File.ReadAllText(absolute);
            if (content.Length > 2200)
            {
                content = content[..2200] + "\n// [entity truncated]";
            }

            contextLines.Add($"Authoritative entity model for index/repository code ({entityName}):");
            contextLines.Add($"File: {entityPath}");
            contextLines.Add(content);
        }
    }

    private static void AppendTestBootstrapContext(string repoPath, List<string> contextLines)
    {
        string? bootstrapContext = TestBootstrapContext.BuildContext(repoPath);
        if (string.IsNullOrWhiteSpace(bootstrapContext))
        {
            return;
        }

        contextLines.Add(bootstrapContext);
    }

    private static void AppendAuthoritativeInfrastructureContracts(string repoPath, List<string> contextLines)
    {
        string? dbStorePath = Directory
            .EnumerateFiles(repoPath, "IDbStore.cs", SearchOption.AllDirectories)
            .FirstOrDefault(path => path.Contains("DbStore", StringComparison.OrdinalIgnoreCase)
                                 && !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(dbStorePath))
        {
            return;
        }

        string content = File.ReadAllText(dbStorePath);
        if (content.Length > 1800)
        {
            content = content[..1800] + "\n// [truncated]";
        }

        contextLines.Add("Authoritative store interface contract (read-only — do not add or change members):");
        contextLines.Add($"File: {Path.GetRelativePath(repoPath, dbStorePath).Replace('\\', '/')}");
        contextLines.Add(content);
    }
}
