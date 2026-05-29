using System.Text;
using workflowX.Infrastructure.Compliance.DotNet;

namespace workflowX.Infrastructure.Rag.DotNet;

/// <summary>
/// .NET/C#-specific RAG context sections — only invoked when <see cref="RepoStack.DotNet"/>.
/// </summary>
internal static class CSharpRagContextSupport
{
    internal static void AppendDotNetImplementationRules(StringBuilder sb)
    {
        sb.AppendLine("- Mirror same-kind RAG exemplars; complete implementations; do not rewrite protected shared contracts.");
        sb.AppendLine("- Call only members declared on injected types in RAG; copy dependency usage from sibling exemplars.");
    }

    internal static void AppendImplementationContext(StringBuilder sb, string repoPath, RepoContract contract)
    {
        AppendSolutionProjects(sb, repoPath);
        AppendProductionSourceExemplars(sb, repoPath);
        AppendTestSourceExemplars(sb, repoPath);
        AppendGroupedFolderExemplars(sb, repoPath);
        AppendDomainTypeConvention(sb, contract);
        TestProjectPackagePromptSupport.AppendRagExemplars(sb, repoPath);

        sb.AppendLine();
        sb.AppendLine(TestBootstrapContext.BuildContext());
    }

    internal static void AppendProductionSourceExemplars(StringBuilder sb, string repoPath)
    {
        IReadOnlyList<string> exemplars = ProductionPathExemplarSupport.DiscoverProductionRelativePaths(repoPath)
            .Where(path => TestProjectPathSupport.TryResolveOwningTestCsproj(repoPath, path) is null)
            .ToList();

        if (exemplars.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine("Production source exemplars (mirror path pattern per kind — change only the feature/type file name):");
        foreach (string path in exemplars
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                     .Take(18))
        {
            sb.AppendLine($"- {path}");
        }
    }

    internal static void AppendTestSourceExemplars(StringBuilder sb, string repoPath)
    {
        IReadOnlyList<string> testPaths = ExemplarTestCompanionSupport.DiscoverTestRelativePaths(repoPath);
        if (testPaths.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine(
            "Test source exemplars (*Tests.cs — copy path and file/class naming; change only the subject segment for the new feature):");
        foreach (string path in testPaths
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                     .Take(18))
        {
            sb.AppendLine($"- {path}");
        }
    }

    internal static void AppendGroupedFolderExemplars(StringBuilder sb, string repoPath)
    {
        IReadOnlyList<string> productionPaths = ProductionPathExemplarSupport
            .DiscoverProductionRelativePaths(repoPath)
            .Where(path => TestProjectPathSupport.TryResolveOwningTestCsproj(repoPath, path) is null)
            .ToList();
        if (productionPaths.Count == 0)
        {
            return;
        }

        IReadOnlyDictionary<string, IReadOnlyList<string>> groups =
            ProductionPathExemplarSupport.GroupPathsByParentFolder(productionPaths);

        if (groups.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine(
            "Grouped folder exemplars (each group is one on-disk folder — plan and implement each new file only inside the group that contains a same-kind exemplar):");
        foreach ((string folder, IReadOnlyList<string> paths) in groups
                     .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                     .Take(14))
        {
            sb.AppendLine($"[{folder}/]");
            foreach (string path in paths.Take(8))
            {
                sb.AppendLine($"  - {path}");
            }
        }
    }

    internal static IEnumerable<string> SemanticQueries()
    {
        yield return "production source patterns validation error handling dependency injection";
        yield return "source file placement folder project structure exemplar";
        yield return "dependency injection composition root registration bootstrap";
        yield return "unit test exemplar Tests.cs naming fixtures mocks";
    }

    private static void AppendDomainTypeConvention(StringBuilder sb, RepoContract contract)
    {
        EntityConvention? entity = contract.Entity;
        if (entity is null)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine(
            "Domain type convention (discovered from repository — copy usings and base/interface from exemplar):");
        sb.AppendLine($"- Folder: {entity.CanonicalDirectory}/");
        sb.AppendLine($"- Required interface: {entity.RequiredInterface}");
        sb.AppendLine($"- Exemplar: {entity.ExemplarRelativePath}");
        if (!string.IsNullOrWhiteSpace(entity.RequiredUsingLine))
        {
            sb.AppendLine($"- Required using (from exemplar): {entity.RequiredUsingLine}");
        }
    }

    private static void AppendSolutionProjects(StringBuilder sb, string repoPath)
    {
        IReadOnlyList<string> projects = SolutionProjectCatalog.GetSolutionProjectRelativePaths(repoPath);
        if (projects.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine("Solution projects (use these exact directory names in paths — do not invent .Api/.Application variants):");
        foreach (string project in projects)
        {
            string? directory = Path.GetDirectoryName(project)?.Replace('\\', '/');
            if (!string.IsNullOrWhiteSpace(directory))
            {
                sb.AppendLine($"- {directory}");
            }
        }
    }
}
