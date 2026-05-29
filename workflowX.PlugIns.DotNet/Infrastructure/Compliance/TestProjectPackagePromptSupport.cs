using System.Text;
using System.Xml.Linq;
using workflowX.Infrastructure.Compliance.DotNet;

namespace workflowX.Infrastructure.Compliance.DotNet;

/// <summary>
/// Prompt + RAG context for test-project PackageReference and ProjectReference (read exemplars; agents update .csproj).
/// </summary>
internal static class TestProjectPackagePromptSupport
{
    private const string RagSectionTitle = "Test project references (packages + project refs)";

    internal static IEnumerable<string> BuildRuleLines()
    {
        yield return
            "All *Tests.cs files must compile against an existing on-disk test .csproj listed in RAG — never against a production (non-test) .csproj.";
        yield return
            "When *Tests.cs uses types from NuGet packages, the files[] output must include the owning test .csproj with matching <PackageReference> entries copied from RAG (same Include and Version pattern as the exemplar).";
        yield return
            "When *Tests.cs uses types from another project, the test .csproj must include <ProjectReference> to that project's .csproj (copy Include path from RAG exemplars).";
        yield return
            $"Copy PackageReference and ProjectReference only from the RAG section \"{RagSectionTitle}\" or sibling test .csproj exemplars — never invent package versions, project paths, or new test projects.";
        yield return
            "When build output says a NuGet type or namespace could not be found, return the full exemplar test .csproj again with that package reference — the test source file is likely in the wrong folder or the wrong .csproj was edited.";
        yield return
            "When build output says a type from a referenced project could not be found, add `using` for the exact namespace from that type's definition in Exemplar sources and ensure ProjectReference targets the project that contains it — references alone do not import namespaces.";
        yield return
            "Return the updated test .csproj (complete file) in the same JSON files[] batch as every *Tests.cs you add or change; append references inside an existing ItemGroup.";
        yield return
            "ProjectReference Include paths are relative to the test .csproj file location; mirror exemplar test projects exactly.";
        yield return
            "Before returning, verify every non-BCL using in *Tests.cs maps to a PackageReference or ProjectReference on the test .csproj you return.";
    }

    internal static void AppendRagExemplars(StringBuilder sb, string repoPath)
    {
        if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
        {
            return;
        }

        IReadOnlyList<TestProjectExemplar> exemplars = CollectTestProjectExemplars(repoPath);
        IReadOnlyList<string> productionProjects = GetSolutionProductionProjects(repoPath);

        if (exemplars.Count == 0 && productionProjects.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine($"{RagSectionTitle} (use ONLY these test projects for *Tests.cs — do not create new test .csproj):");
        foreach (TestProjectExemplar exemplar in exemplars)
        {
            sb.AppendLine($"- {exemplar.RelativePath}");
            foreach (string projectRef in exemplar.ProjectReferences.Take(12))
            {
                sb.AppendLine($"  - ProjectReference Include=\"{projectRef}\"");
            }

            foreach ((string id, string version) in exemplar.Packages.Take(16))
            {
                sb.AppendLine($"  - PackageReference Include=\"{id}\" Version=\"{version}\"");
            }
        }

        if (productionProjects.Count > 0)
        {
            sb.AppendLine("- Solution production projects (ProjectReference targets for tests — not locations for *Tests.cs):");
            foreach (string project in productionProjects.Take(12))
            {
                sb.AppendLine($"  - {project}");
            }
        }
    }

    private static IReadOnlyList<TestProjectExemplar> CollectTestProjectExemplars(string repoPath)
    {
        string repoRoot = Path.GetFullPath(repoPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var exemplars = new List<TestProjectExemplar>();

        foreach (string absolute in Directory.EnumerateFiles(repoPath, "*.csproj", SearchOption.AllDirectories))
        {
            string normalized = absolute.Replace('\\', '/');
            if (normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!TestProjectPathSupport.IsTestProjectCsproj(absolute))
            {
                continue;
            }

            List<(string Id, string Version)> packages = ReadPackageReferences(absolute);
            List<string> projectRefs = ReadProjectReferenceIncludes(absolute);
            string relative = Path.GetRelativePath(repoRoot, absolute).Replace('\\', '/');
            exemplars.Add(new TestProjectExemplar(relative, packages, projectRefs));
            if (exemplars.Count >= 6)
            {
                break;
            }
        }

        return exemplars;
    }

    private static IReadOnlyList<string> GetSolutionProductionProjects(string repoPath)
    {
        return SolutionProjectCatalog.GetSolutionProjectRelativePaths(repoPath)
            .Where(path => !TestProjectPathSupport.IsTestProjectCsproj(Path.Combine(repoPath, path.Replace('/', Path.DirectorySeparatorChar))))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<(string Id, string Version)> ReadPackageReferences(string csprojAbsolute)
    {
        var packages = new List<(string Id, string Version)>();
        if (!TryLoadProject(csprojAbsolute, out XDocument? document))
        {
            return packages;
        }

        IReadOnlyDictionary<string, string> centralVersions = LoadCentralPackageVersions(csprojAbsolute);
        foreach (XElement reference in document!.Descendants("PackageReference"))
        {
            string? id = reference.Attribute("Include")?.Value;
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            string? version = reference.Attribute("Version")?.Value
                ?? reference.Element("Version")?.Value;
            if (string.IsNullOrWhiteSpace(version)
                && centralVersions.TryGetValue(id, out string? centralVersion))
            {
                version = centralVersion;
            }

            if (string.IsNullOrWhiteSpace(version))
            {
                continue;
            }

            packages.Add((id, version));
        }

        return packages;
    }

    private static IReadOnlyDictionary<string, string> LoadCentralPackageVersions(string csprojAbsolute)
    {
        var versions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? directory = Path.GetDirectoryName(csprojAbsolute);
        for (int depth = 0; depth < 8 && !string.IsNullOrWhiteSpace(directory); depth++)
        {
            string propsPath = Path.Combine(directory, "Directory.Packages.props");
            if (File.Exists(propsPath) && TryLoadProject(propsPath, out XDocument? document))
            {
                foreach (XElement packageVersion in document!.Descendants("PackageVersion"))
                {
                    string? id = packageVersion.Attribute("Include")?.Value;
                    string? version = packageVersion.Attribute("Version")?.Value;
                    if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(version))
                    {
                        versions[id] = version;
                    }
                }

                return versions;
            }

            directory = Path.GetDirectoryName(directory);
        }

        return versions;
    }

    private static List<string> ReadProjectReferenceIncludes(string csprojAbsolute)
    {
        var references = new List<string>();
        if (!TryLoadProject(csprojAbsolute, out XDocument? document))
        {
            return references;
        }

        foreach (XElement reference in document!.Descendants("ProjectReference"))
        {
            string? include = reference.Attribute("Include")?.Value;
            if (string.IsNullOrWhiteSpace(include))
            {
                continue;
            }

            references.Add(include.Replace('/', '\\'));
        }

        return references;
    }

    private static bool TryLoadProject(string csprojAbsolute, out XDocument? document)
    {
        document = null;
        try
        {
            document = XDocument.Load(csprojAbsolute);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private sealed record TestProjectExemplar(
        string RelativePath,
        List<(string Id, string Version)> Packages,
        List<string> ProjectReferences);
}
