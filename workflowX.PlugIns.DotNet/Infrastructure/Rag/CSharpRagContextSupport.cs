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

        sb.AppendLine();
        sb.AppendLine(TestBootstrapContext.BuildContext());
    }

    internal static IEnumerable<string> SemanticQueries()
    {
        yield return "controller repository entity index patterns validation and error handling";
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
