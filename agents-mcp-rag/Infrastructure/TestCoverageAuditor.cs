namespace agents_mcp_rag.Infrastructure;

internal static class TestCoverageAuditor
{
    internal static List<AgentFinding> ValidateMissingTests(WorkflowState state)
    {
        var findings = new List<AgentFinding>();
        string? testsDir = DetectRepositoryTestsDirectory(state.RepoPath);
        if (string.IsNullOrWhiteSpace(testsDir))
        {
            return findings;
        }

        if (!HasRepositoryTestConvention(state.RepoPath, testsDir))
        {
            return findings;
        }

        var proposedFiles = GetAllProposedFiles(state);
        var proposedPaths = new HashSet<string>(
            proposedFiles.Select(f => f.RelativePath.Replace('\\', '/')),
            StringComparer.OrdinalIgnoreCase);

        var entitiesToValidate = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var repoImplPath in proposedPaths.Where(IsRepositoryImplementationPath))
        {
            string? entity = ExtractRepositoryEntityName(repoImplPath);
            if (!string.IsNullOrWhiteSpace(entity))
            {
                entitiesToValidate.Add(entity);
            }
        }

        foreach (string entity in entitiesToValidate)
        {
            if (state.DeferredTestEntities.Contains(entity))
            {
                continue;
            }

            if (!RepositoryImplementationExists(state.RepoPath, entity))
            {
                continue;
            }

            TryAddMissingTestFinding(state, testsDir, entity, findings);
        }

        return findings;
    }

    internal static string? GetRepositoryTestsDirectory(string repoPath)
    {
        return DetectRepositoryTestsDirectory(repoPath);
    }

    internal static string? BuildExpectedRepositoryTestPath(string repoPath, string entityName)
    {
        string? testsDir = DetectRepositoryTestsDirectory(repoPath);
        if (string.IsNullOrWhiteSpace(testsDir))
        {
            return null;
        }

        return $"{testsDir}/{entityName}RepositoryTests.cs";
    }

    private static void TryAddMissingTestFinding(
        WorkflowState state,
        string testsDir,
        string entity,
        List<AgentFinding> findings)
    {
        if (HasRepositoryTest(state, testsDir, entity))
        {
            return;
        }

        string expectedPath = $"{testsDir}/{entity}RepositoryTests.cs";
        findings.Add(new AgentFinding
        {
            Severity = FindingSeverity.High,
            Message = $"Missing unit test for {entity} repository: expected {expectedPath} following existing RepositoryTest conventions."
        });
    }

    private static bool HasRepositoryTest(WorkflowState state, string testsDir, string entity)
    {
        string expectedFileName = $"{entity}RepositoryTests.cs";
        var proposedFiles = GetAllProposedFiles(state);
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

    private static bool HasRepositoryTestConvention(string repoPath, string testsDir)
    {
        string testsAbsolutePath = Path.Combine(repoPath, testsDir.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(testsAbsolutePath))
        {
            return false;
        }

        return Directory
            .EnumerateFiles(testsAbsolutePath, "*RepositoryTests.cs", SearchOption.TopDirectoryOnly)
            .Any();
    }

    private static string? DetectRepositoryTestsDirectory(string repoPath)
    {
        return Directory
            .EnumerateDirectories(repoPath, "RepositoryTest", SearchOption.AllDirectories)
            .Select(path => ToRelativePath(repoPath, path))
            .Where(relative => relative.Contains(".UnitTest/", StringComparison.OrdinalIgnoreCase)
                            || relative.Contains("UnitTest/", StringComparison.OrdinalIgnoreCase))
            .OrderBy(relative => relative.Length)
            .FirstOrDefault();
    }

    private static bool RepositoryImplementationExists(string repoPath, string entity)
    {
        string expectedFileName = $"{entity}Repository.cs";
        return Directory
            .EnumerateFiles(repoPath, expectedFileName, SearchOption.AllDirectories)
            .Any(path => IsRepositoryImplementationPath(ToRelativePath(repoPath, path)));
    }

    private static bool IsRepositoryImplementationPath(string path)
    {
        string fileName = Path.GetFileName(path);
        return fileName.EndsWith("Repository.cs", StringComparison.OrdinalIgnoreCase)
               && !fileName.StartsWith('I');
    }

    private static string? ExtractRepositoryEntityName(string repositoryPath)
    {
        string fileName = Path.GetFileName(repositoryPath);
        if (!fileName.EndsWith("Repository.cs", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string entity = fileName[..^"Repository.cs".Length];
        return string.IsNullOrWhiteSpace(entity) ? null : entity;
    }

    private static List<GeneratedFile> GetAllProposedFiles(WorkflowState state)
    {
        return (state.Backend?.ProposedFiles ?? new List<GeneratedFile>())
            .Concat(state.Recovery?.ProposedFiles ?? new List<GeneratedFile>())
            .ToList();
    }

    private static string ToRelativePath(string repoPath, string absolutePath)
    {
        return Path.GetRelativePath(repoPath, absolutePath).Replace('\\', '/');
    }
}
