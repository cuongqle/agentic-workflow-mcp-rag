namespace agents_mcp_rag.Infrastructure.Compliance.DotNet;

internal sealed record TestConvention(
    string TestDirectory,
    string ProductionFileSuffix,
    int ExemplarCount);

internal static class TestCoverageAuditor
{
    internal static List<AgentFinding> ValidateMissingTests(WorkflowState state)
    {
        var findings = new List<AgentFinding>();
        var conventions = DiscoverTestConventions(state.RepoPath);
        if (conventions.Count == 0)
        {
            return findings;
        }

        var proposedPaths = new HashSet<string>(
            ProposedFileSupport.GetAllProposedFiles(state).Select(f => f.RelativePath.Replace('\\', '/')),
            StringComparer.OrdinalIgnoreCase);

        foreach (var convention in conventions)
        {
            var productionPaths = proposedPaths
                .Where(p => MatchesProductionSuffix(p, convention.ProductionFileSuffix))
                .ToList();

            foreach (var path in productionPaths)
            {
                string? subjectBaseName = ExtractProductionBaseName(path, convention.ProductionFileSuffix);
                if (string.IsNullOrWhiteSpace(subjectBaseName))
                {
                    continue;
                }

                if (state.DeferredTestEntities.Contains(subjectBaseName))
                {
                    continue;
                }

                if (!ProductionFileExists(state.RepoPath, subjectBaseName, convention.ProductionFileSuffix))
                {
                    continue;
                }

                TryAddMissingTestFinding(state, convention, subjectBaseName, findings);
            }
        }

        return findings;
    }

    internal static IReadOnlyList<TestConvention> DiscoverTestConventions(string repoPath)
    {
        var conventions = new Dictionary<string, TestConvention>(StringComparer.OrdinalIgnoreCase);
        foreach (var testFile in EnumerateTestFiles(repoPath))
        {
            string testDir = Path.GetDirectoryName(testFile.RelativePath)?.Replace('\\', '/') ?? string.Empty;
            if (string.IsNullOrWhiteSpace(testDir))
            {
                continue;
            }

            string? productionBaseName = ExtractSubjectBaseNameFromTestFile(testFile.FileName);
            if (string.IsNullOrWhiteSpace(productionBaseName))
            {
                continue;
            }

            string productionFileName = productionBaseName + ".cs";
            if (!ProductionFileExistsInRepo(repoPath, productionFileName, productionBaseName))
            {
                continue;
            }

            string suffix = InferProductionSuffix(productionFileName);
            string key = $"{testDir}::{suffix}";
            if (!conventions.TryGetValue(key, out var convention))
            {
                conventions[key] = new TestConvention(testDir, suffix, 1);
            }
            else
            {
                conventions[key] = convention with { ExemplarCount = convention.ExemplarCount + 1 };
            }
        }

        return conventions.Values
            .Where(c => c.ExemplarCount >= 1)
            .OrderByDescending(c => c.ExemplarCount)
            .ThenBy(c => c.TestDirectory, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void TryAddMissingTestFinding(
        WorkflowState state,
        TestConvention convention,
        string productionBaseName,
        List<AgentFinding> findings)
    {
        if (HasMatchingTest(state, convention.TestDirectory, productionBaseName))
        {
            return;
        }

        string expectedPath = $"{convention.TestDirectory}/{productionBaseName}Tests.cs";
        string layer = DescribeLayer(convention.ProductionFileSuffix);
        findings.Add(new AgentFinding
        {
            Severity = FindingSeverity.High,
            Message = $"Missing unit test for {productionBaseName} ({layer}): expected {expectedPath} following existing *Tests.cs conventions."
        });
    }

    private static bool HasMatchingTest(WorkflowState state, string testsDir, string productionBaseName)
    {
        string expectedFileName = $"{productionBaseName}Tests.cs";
        var proposedFiles = ProposedFileSupport.GetAllProposedFiles(state);
        if (proposedFiles.Any(file => Path.GetFileName(file.RelativePath)
                .Equals(expectedFileName, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        string expectedAbsolutePath = Path.Combine(
            state.RepoPath,
            testsDir.Replace('/', Path.DirectorySeparatorChar),
            expectedFileName);
        if (File.Exists(expectedAbsolutePath))
        {
            return true;
        }

        return Directory
            .EnumerateFiles(state.RepoPath, expectedFileName, SearchOption.AllDirectories)
            .Any(path => ToRelativePath(state.RepoPath, path)
                .Contains(testsDir, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ProductionFileExists(string repoPath, string productionBaseName, string productionSuffix)
    {
        string fileName = productionBaseName + ".cs";
        return Directory
            .EnumerateFiles(repoPath, fileName, SearchOption.AllDirectories)
            .Any(path => !IsTestArtifactPath(ToRelativePath(repoPath, path))
                        && MatchesProductionSuffix(ToRelativePath(repoPath, path), productionSuffix));
    }

    private static bool ProductionFileExistsInRepo(string repoPath, string productionFileName, string productionBaseName)
    {
        return Directory
            .EnumerateFiles(repoPath, productionFileName, SearchOption.AllDirectories)
            .Any(path => !IsTestArtifactPath(ToRelativePath(repoPath, path))
                        && Path.GetFileNameWithoutExtension(path)
                            .Equals(productionBaseName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesProductionSuffix(string relativePath, string productionSuffix)
    {
        string fileName = Path.GetFileName(relativePath);
        if (!fileName.EndsWith(productionSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (productionSuffix.Equals("Repository.cs", StringComparison.OrdinalIgnoreCase)
            && fileName.StartsWith('I'))
        {
            return false;
        }

        return !fileName.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase)
               && !IsTestArtifactPath(relativePath);
    }

    private static string? ExtractProductionBaseName(string relativePath, string productionSuffix)
    {
        string fileName = Path.GetFileName(relativePath);
        if (!fileName.EndsWith(productionSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Path.GetFileNameWithoutExtension(fileName);
    }

    internal static string? ExtractProductionBaseNameFromTestFileName(string testFileName) =>
        ExtractSubjectBaseNameFromTestFile(testFileName);

    private static string? ExtractSubjectBaseNameFromTestFile(string testFileName)
    {
        if (!testFileName.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string baseName = Path.GetFileNameWithoutExtension(testFileName);
        if (baseName.EndsWith("Tests", StringComparison.OrdinalIgnoreCase))
        {
            return baseName[..^"Tests".Length];
        }

        return null;
    }

    private static string InferProductionSuffix(string productionFileName)
    {
        if (productionFileName.EndsWith("Repository.cs", StringComparison.OrdinalIgnoreCase))
        {
            return "Repository.cs";
        }

        if (productionFileName.EndsWith("Service.cs", StringComparison.OrdinalIgnoreCase))
        {
            return "Service.cs";
        }

        if (productionFileName.EndsWith("Controller.cs", StringComparison.OrdinalIgnoreCase))
        {
            return "Controller.cs";
        }

        return Path.GetExtension(productionFileName).Length > 0
            ? Path.GetFileName(productionFileName)
            : productionFileName;
    }

    private static string DescribeLayer(string productionSuffix)
    {
        return productionSuffix switch
        {
            "Repository.cs" => "repository",
            "Service.cs" => "service",
            "Controller.cs" => "controller",
            _ => "component"
        };
    }

    private static IEnumerable<(string RelativePath, string FileName)> EnumerateTestFiles(string repoPath)
    {
        foreach (var absolute in Directory.EnumerateFiles(repoPath, "*Tests.cs", SearchOption.AllDirectories))
        {
            string normalized = absolute.Replace('\\', '/');
            if (normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string relative = ToRelativePath(repoPath, absolute);
            if (!IsTestArtifactPath(relative))
            {
                continue;
            }

            yield return (relative, Path.GetFileName(relative));
        }
    }

    private static bool IsTestArtifactPath(string relativePath)
    {
        return BuildFailureClassifier.IsTestArtifactPath(relativePath);
    }

    private static string ToRelativePath(string repoPath, string absolutePath)
    {
        return Path.GetRelativePath(repoPath, absolutePath).Replace('\\', '/');
    }

    internal static bool MatchesProductionSuffixForTests(string relativePath, string productionSuffix) =>
        MatchesProductionSuffix(relativePath, productionSuffix);

    internal static string? ExtractProductionBaseNameForTests(string relativePath, string productionSuffix) =>
        ExtractProductionBaseName(relativePath, productionSuffix);

    internal static bool ProductionFileExistsForTests(string repoPath, string productionBaseName, string productionSuffix) =>
        ProductionFileExists(repoPath, productionBaseName, productionSuffix);

    internal static bool HasMatchingTestFile(WorkflowState state, string testsDir, string productionBaseName) =>
        HasMatchingTest(state, testsDir, productionBaseName);
}
