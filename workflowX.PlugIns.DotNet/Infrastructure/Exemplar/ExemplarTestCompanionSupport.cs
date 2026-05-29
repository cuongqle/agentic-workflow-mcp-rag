namespace workflowX.Infrastructure.Exemplar.DotNet;

/// <summary>
/// Discovers *Tests.cs companions for planned production deliverables from on-disk exemplar pairs.
/// </summary>
internal static class ExemplarTestCompanionSupport
{
    internal static IReadOnlyList<string> DiscoverMissingTestPaths(
        string repoPath,
        IReadOnlyList<string> plannedPaths)
    {
        if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath) || plannedPaths.Count == 0)
        {
            return Array.Empty<string>();
        }

        IReadOnlyList<string> productionPaths = ProductionPathExemplarSupport.DiscoverProductionRelativePaths(repoPath);
        IReadOnlyList<string> testPaths = DiscoverTestRelativePaths(repoPath);
        if (productionPaths.Count == 0 || testPaths.Count == 0)
        {
            return Array.Empty<string>();
        }

        var plannedSet = new HashSet<string>(
            plannedPaths.Select(ProductionPathExemplarSupport.NormalizePath),
            StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();

        foreach (string plannedPath in plannedPaths)
        {
            if (IsTestSourcePath(plannedPath))
            {
                continue;
            }

            if (!ShouldHaveTestCompanion(repoPath, plannedPath, productionPaths, testPaths))
            {
                continue;
            }

            string? plannedTestPath = DiscoverPlannedTestPath(repoPath, plannedPath, productionPaths, testPaths);
            if (string.IsNullOrWhiteSpace(plannedTestPath))
            {
                continue;
            }

            if (plannedSet.Add(plannedTestPath))
            {
                missing.Add(plannedTestPath);
            }
        }

        return missing;
    }

    internal static string? DiscoverExpectedTestPath(
        string repoPath,
        string productionRelativePath)
    {
        if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
        {
            return null;
        }

        IReadOnlyList<string> productionPaths = ProductionPathExemplarSupport.DiscoverProductionRelativePaths(repoPath);
        IReadOnlyList<string> testPaths = DiscoverTestRelativePaths(repoPath);
        if (testPaths.Count == 0)
        {
            return null;
        }

        return DiscoverPlannedTestPath(repoPath, productionRelativePath, productionPaths, testPaths);
    }

    internal static string? DiscoverTestExemplarPath(
        string repoPath,
        string plannedTestPath,
        IReadOnlyList<string> productionPaths,
        IReadOnlyList<string> testPaths)
    {
        string normalizedPlannedTest = ProductionPathExemplarSupport.NormalizePath(plannedTestPath);
        if (!TryGetSubjectFromTestPath(normalizedPlannedTest, out string plannedSubject))
        {
            return null;
        }

        foreach (string testPath in testPaths)
        {
            if (!TryGetSubjectFromTestPath(testPath, out string exemplarSubject))
            {
                continue;
            }

            if (exemplarSubject.Equals(plannedSubject, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string candidate = SwapSubjectInRelativePath(testPath, exemplarSubject, plannedSubject);
            if (candidate.Equals(normalizedPlannedTest, StringComparison.OrdinalIgnoreCase))
            {
                return testPath;
            }
        }

        return null;
    }

    internal static IEnumerable<AgentFinding> ValidatePlannedProductionTests(
        string repoPath,
        ArchitecturePlan? plan)
    {
        if (plan is null || string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
        {
            yield break;
        }

        IReadOnlyList<string> plannedPaths = plan.BackendPaths;
        IReadOnlyList<string> productionPaths = ProductionPathExemplarSupport.DiscoverProductionRelativePaths(repoPath);
        IReadOnlyList<string> testPaths = DiscoverTestRelativePaths(repoPath);
        if (testPaths.Count == 0)
        {
            yield break;
        }

        var plannedSet = new HashSet<string>(plannedPaths, StringComparer.OrdinalIgnoreCase);
        foreach (string plannedPath in plannedPaths)
        {
            if (IsTestSourcePath(plannedPath))
            {
                continue;
            }

            if (!ShouldHaveTestCompanion(repoPath, plannedPath, productionPaths, testPaths))
            {
                continue;
            }

            string? expectedTest = DiscoverPlannedTestPath(repoPath, plannedPath, productionPaths, testPaths);
            if (string.IsNullOrWhiteSpace(expectedTest))
            {
                continue;
            }

            if (plannedSet.Contains(expectedTest))
            {
                continue;
            }

            yield return new AgentFinding
            {
                Severity = FindingSeverity.High,
                Message =
                    $"Architecture plan lists production '{plannedPath}' but no matching *Tests.cs "
                    + $"(expected '{expectedTest}' from on-disk test exemplar naming)."
            };
        }
    }

    private static bool ShouldHaveTestCompanion(
        string repoPath,
        string plannedProductionPath,
        IReadOnlyList<string> productionPaths,
        IReadOnlyList<string> testPaths)
    {
        string? productionExemplar = FindProductionExemplarPath(plannedProductionPath, productionPaths);
        if (string.IsNullOrWhiteSpace(productionExemplar))
        {
            return false;
        }

        return TryFindPairedTestPath(repoPath, productionExemplar, testPaths) is not null;
    }

    private static string? DiscoverPlannedTestPath(
        string repoPath,
        string plannedProductionPath,
        IReadOnlyList<string> productionPaths,
        IReadOnlyList<string> testPaths)
    {
        string normalizedPlanned = ProductionPathExemplarSupport.NormalizePath(plannedProductionPath);
        string? productionExemplar = FindProductionExemplarPath(normalizedPlanned, productionPaths);
        if (string.IsNullOrWhiteSpace(productionExemplar))
        {
            return null;
        }

        string? testExemplar = TryFindPairedTestPath(repoPath, productionExemplar, testPaths);
        if (string.IsNullOrWhiteSpace(testExemplar))
        {
            return null;
        }

        if (!TryResolveSubjects(normalizedPlanned, productionExemplar, out string plannedSubject, out string exemplarSubject))
        {
            return null;
        }

        return SwapSubjectInRelativePath(testExemplar, exemplarSubject, plannedSubject);
    }

    private static string? FindProductionExemplarPath(
        string plannedProductionPath,
        IReadOnlyList<string> productionPaths)
    {
        string normalizedPlanned = ProductionPathExemplarSupport.NormalizePath(plannedProductionPath);
        if (TryGetRoleSuffixFromPath(normalizedPlanned, out string plannedRole))
        {
            string? plannedParent = Path.GetDirectoryName(normalizedPlanned)?.Replace('\\', '/');
            return productionPaths
                .Where(path => !path.Equals(normalizedPlanned, StringComparison.OrdinalIgnoreCase))
                .Where(path => TryGetRoleSuffixFromPath(path, out string role)
                    && role.Equals(plannedRole, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(path => SharesParentDirectory(path, plannedParent))
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        string? parent = Path.GetDirectoryName(normalizedPlanned)?.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(parent))
        {
            return null;
        }

        return productionPaths
            .Where(path => !path.Equals(normalizedPlanned, StringComparison.OrdinalIgnoreCase))
            .Where(path => SharesParentDirectory(path, parent))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static string? TryFindPairedTestPath(
        string repoPath,
        string productionExemplarPath,
        IReadOnlyList<string> testPaths)
    {
        string productionStem = Path.GetFileNameWithoutExtension(Path.GetFileName(productionExemplarPath));
        string expectedFileName = productionStem + "Tests.cs";
        string? byName = testPaths.FirstOrDefault(path =>
            Path.GetFileName(path).Equals(expectedFileName, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(byName))
        {
            return byName;
        }

        if (DeliverableFileNameSupport.TryGetSubjectAndRole(productionStem, out string subject, out _))
        {
            string subjectTests = subject + "Tests.cs";
            byName = testPaths.FirstOrDefault(path =>
                Path.GetFileName(path).Equals(subjectTests, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(byName))
            {
                return byName;
            }
        }
        else if (productionStem.Length > 0)
        {
            string subjectTests = productionStem + "Tests.cs";
            byName = testPaths.FirstOrDefault(path =>
                Path.GetFileName(path).Equals(subjectTests, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(byName))
            {
                return byName;
            }
        }

        string absolute = Path.Combine(repoPath, productionExemplarPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(absolute))
        {
            return null;
        }

        string content = File.ReadAllText(absolute);
        return testPaths.FirstOrDefault(path =>
        {
            string testStem = Path.GetFileNameWithoutExtension(Path.GetFileName(path));
            return content.Contains(testStem, StringComparison.Ordinal)
                || content.Contains(productionStem, StringComparison.Ordinal);
        });
    }

    private static bool TryResolveSubjects(
        string plannedProductionPath,
        string productionExemplarPath,
        out string plannedSubject,
        out string exemplarSubject)
    {
        plannedSubject = string.Empty;
        exemplarSubject = string.Empty;
        string plannedStem = Path.GetFileNameWithoutExtension(Path.GetFileName(plannedProductionPath));
        string exemplarStem = Path.GetFileNameWithoutExtension(Path.GetFileName(productionExemplarPath));

        if (DeliverableFileNameSupport.TryGetSubjectAndRole(plannedStem, out plannedSubject, out _)
            && DeliverableFileNameSupport.TryGetSubjectAndRole(exemplarStem, out exemplarSubject, out _))
        {
            return true;
        }

        plannedSubject = plannedStem;
        exemplarSubject = exemplarStem;
        return !string.IsNullOrWhiteSpace(plannedSubject) && !string.IsNullOrWhiteSpace(exemplarSubject);
    }

    private static string SwapSubjectInRelativePath(string relativePath, string fromSubject, string toSubject)
    {
        string normalized = ProductionPathExemplarSupport.NormalizePath(relativePath);
        string? directory = Path.GetDirectoryName(normalized)?.Replace('\\', '/');
        string fileName = Path.GetFileName(normalized);
        string stem = Path.GetFileNameWithoutExtension(fileName);
        string extension = Path.GetExtension(fileName);
        string newStem = ReplaceLeadingSubject(stem, fromSubject, toSubject);
        string newFile = newStem + extension;
        return string.IsNullOrWhiteSpace(directory) ? newFile : $"{directory}/{newFile}";
    }

    private static string ReplaceLeadingSubject(string stem, string fromSubject, string toSubject)
    {
        if (stem.StartsWith(fromSubject, StringComparison.OrdinalIgnoreCase)
            && stem.Length > fromSubject.Length)
        {
            return toSubject + stem[fromSubject.Length..];
        }

        if (stem.Equals(fromSubject, StringComparison.OrdinalIgnoreCase))
        {
            return toSubject;
        }

        return stem;
    }

    private static bool TryGetSubjectFromTestPath(string testPath, out string subject)
    {
        subject = string.Empty;
        string stem = Path.GetFileNameWithoutExtension(Path.GetFileName(testPath));
        if (!stem.EndsWith("Tests", StringComparison.Ordinal) || stem.Length <= "Tests".Length)
        {
            return false;
        }

        string withoutTests = stem[..^"Tests".Length];
        if (DeliverableFileNameSupport.TryGetSubjectAndRole(withoutTests, out subject, out _))
        {
            return true;
        }

        subject = withoutTests;
        return !string.IsNullOrWhiteSpace(subject);
    }

    internal static IReadOnlyList<string> DiscoverTestRelativePaths(string repoPath)
    {
        if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
        {
            return Array.Empty<string>();
        }

        string repoRoot = Path.GetFullPath(repoPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var paths = new List<string>();
        foreach (string absolute in Directory.EnumerateFiles(repoPath, "*Tests.cs", SearchOption.AllDirectories))
        {
            string normalized = absolute.Replace('\\', '/');
            if (normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            paths.Add(Path.GetRelativePath(repoRoot, absolute).Replace('\\', '/'));
        }

        return paths;
    }

    private static bool IsTestSourcePath(string path) =>
        ProductionPathExemplarSupport.NormalizePath(path)
            .EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase);

    private static bool TryGetRoleSuffixFromPath(string relativePath, out string roleSuffix)
    {
        roleSuffix = string.Empty;
        string stem = Path.GetFileNameWithoutExtension(Path.GetFileName(relativePath));
        return DeliverableFileNameSupport.TryGetRoleSuffix(stem, out roleSuffix);
    }

    private static bool SharesParentDirectory(string path, string? parentDirectory)
    {
        if (string.IsNullOrWhiteSpace(parentDirectory))
        {
            return false;
        }

        string? pathParent = Path.GetDirectoryName(path.Replace('\\', '/'))?.Replace('\\', '/');
        return parentDirectory.Equals(pathParent, StringComparison.OrdinalIgnoreCase);
    }
}
