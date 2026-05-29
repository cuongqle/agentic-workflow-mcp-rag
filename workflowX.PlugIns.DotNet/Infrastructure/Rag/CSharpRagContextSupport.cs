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
        sb.AppendLine("- Mirror RAG exemplars; complete implementations; do not rewrite bootstrap/store contracts.");
    }

    internal static void AppendImplementationContext(StringBuilder sb, string repoPath)
    {
        AppendSolutionProjects(sb, repoPath);
        AppendProductionSourceExemplars(sb, repoPath);
        TestProjectPackagePromptSupport.AppendRagExemplars(sb, repoPath);

        sb.AppendLine();
        sb.AppendLine(TestBootstrapContext.BuildContext());
    }

    internal static void AppendProductionSourceExemplars(StringBuilder sb, string repoPath)
    {
        if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
        {
            return;
        }

        string repoRoot = Path.GetFullPath(repoPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var exemplars = new List<string>();

        foreach (string absolute in Directory.EnumerateFiles(repoPath, "*.cs", SearchOption.AllDirectories))
        {
            string normalized = absolute.Replace('\\', '/');
            if (normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string relative = Path.GetRelativePath(repoRoot, absolute).Replace('\\', '/');
            if (relative.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase)
                || relative.EndsWith(".AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TestProjectPathSupport.TryResolveOwningTestCsproj(repoRoot, relative) is not null)
            {
                continue;
            }

            exemplars.Add(relative);
        }

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

    internal static IEnumerable<string> SemanticQueries()
    {
        yield return "controller repository entity index patterns validation and error handling";
        yield return "entity domain model class source file";
        yield return "dependency injection composition root registration bootstrap";
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
