using agents_mcp_rag.Orchestration;

namespace agents_mcp_rag.Infrastructure.Compliance.DotNet;

/// <summary>
/// Derives expected *Tests.cs paths from architecture deliverables and clones missing tests from repo exemplars.
/// </summary>
public static class MissingLayerTestSynthesizer
{
    public static IReadOnlyList<string> GetRequiredTestPaths(WorkflowState state) =>
        EnumerateExpectedTestSubjects(state, requireProductionOnDisk: false)
            .Select(entry => FormatTestPath(entry.Convention, entry.ProductionBaseName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static IReadOnlyList<GeneratedFile> SynthesizeMissingTests(WorkflowState state)
    {
        var files = new List<GeneratedFile>();
        foreach ((TestConvention convention, string productionBaseName) in EnumerateExpectedTestSubjects(state, requireProductionOnDisk: true))
        {
            if (!LayerTestTemplateBuilder.TryBuildFromExemplar(
                    state.RepoPath,
                    productionBaseName,
                    convention,
                    out string content))
            {
                continue;
            }

            files.Add(new GeneratedFile
            {
                RelativePath = FormatTestPath(convention, productionBaseName),
                Content = content
            });
        }

        return files;
    }

    private static IEnumerable<(TestConvention Convention, string ProductionBaseName)> EnumerateExpectedTestSubjects(
        WorkflowState state,
        bool requireProductionOnDisk)
    {
        IReadOnlyList<TestConvention> conventions = TestCoverageAuditor.DiscoverTestConventions(state.RepoPath);
        if (conventions.Count == 0)
        {
            yield break;
        }

        foreach (string path in CollectCandidateProductionPaths(state))
        {
            foreach (TestConvention convention in conventions)
            {
                if (!TestCoverageAuditor.MatchesProductionSuffixForTests(path, convention.ProductionFileSuffix))
                {
                    continue;
                }

                string? baseName = TestCoverageAuditor.ExtractProductionBaseNameForTests(path, convention.ProductionFileSuffix);
                if (string.IsNullOrWhiteSpace(baseName))
                {
                    continue;
                }

                if (state.DeferredTestEntities.Contains(baseName))
                {
                    continue;
                }

                if (requireProductionOnDisk
                    && !TestCoverageAuditor.ProductionFileExistsForTests(state.RepoPath, baseName, convention.ProductionFileSuffix))
                {
                    continue;
                }

                yield return (convention, baseName);
            }
        }
    }

    private static IEnumerable<string> CollectCandidateProductionPaths(WorkflowState state)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (state.ArchitecturePlan?.BackendPaths is { Count: > 0 } backendPaths)
        {
            foreach (string path in backendPaths)
            {
                paths.Add(path.Replace('\\', '/'));
            }
        }

        foreach (GeneratedFile file in ProposedFileSupport.GetAllProposedFiles(state))
        {
            if (file.RelativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                paths.Add(file.RelativePath.Replace('\\', '/'));
            }
        }

        foreach (string path in state.AppliedFiles)
        {
            paths.Add(path.Replace('\\', '/'));
        }

        return paths;
    }

    private static string FormatTestPath(TestConvention convention, string productionBaseName) =>
        $"{convention.TestDirectory}/{productionBaseName}Tests.cs".Replace('\\', '/');
}
