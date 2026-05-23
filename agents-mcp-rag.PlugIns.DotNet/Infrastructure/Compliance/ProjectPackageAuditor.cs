using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace agents_mcp_rag.Infrastructure.Compliance.DotNet;

/// <summary>
/// Keeps test/production projects aligned with NuGet packages referenced by generated code and build errors (CS0246).
/// </summary>
internal static class ProjectPackageAuditor
{
    private static readonly Regex UsingRegex = new(
        @"^\s*using\s+([A-Za-z_][A-Za-z0-9_.]*)\s*;",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex Cs0246Regex = new(
        @"type or namespace name\s+'([A-Za-z_][A-Za-z0-9_]*)'\s+could not be found",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Dictionary<string, string> RootNamespaceToPackageId = new(StringComparer.Ordinal)
    {
        ["Moq"] = "Moq",
        ["Xunit"] = "xunit",
        ["NSubstitute"] = "NSubstitute",
        ["FluentAssertions"] = "FluentAssertions",
        ["AutoFixture"] = "AutoFixture",
        ["Bogus"] = "Bogus",
        ["Newtonsoft"] = "Newtonsoft.Json"
    };

    internal static string? BuildTestPackageContext(string repoPath)
    {
        var packageIds = DiscoverTestPackageIds(repoPath);
        var roots = DiscoverAllowedTestRootNamespaces(repoPath);
        if (packageIds.Count == 0 && roots.Count == 0)
        {
            return null;
        }

        var sb = new StringBuilder();
        sb.AppendLine("Test package conventions (discovered from this repository):");
        if (packageIds.Count > 0)
        {
            sb.AppendLine($"- Referenced test packages: {string.Join(", ", packageIds.OrderBy(p => p, StringComparer.Ordinal))}");
        }

        if (roots.Count > 0)
        {
            sb.AppendLine(
                $"- Allowed test usings (mirror sibling *Tests.cs): {string.Join(", ", roots.OrderBy(r => r, StringComparer.Ordinal))}");
        }

        sb.AppendLine("- Prefer HotSpot/bootstrap integration tests when exemplars do not use mocking libraries.");
        sb.AppendLine("- If a new package is required, workflow will run dotnet add package on the test .csproj automatically.");
        return sb.ToString();
    }

    internal static bool TryValidateTestPackages(
        string repoPath,
        string relativePath,
        string content,
        out string reason)
    {
        reason = string.Empty;
        if (!IsTestSourcePath(relativePath))
        {
            return true;
        }

        var allowedRoots = DiscoverAllowedTestRootNamespaces(repoPath);
        if (allowedRoots.Count == 0)
        {
            return true;
        }

        foreach (string root in ExtractUsingRoots(content))
        {
            if (!RootNamespaceToPackageId.ContainsKey(root))
            {
                continue;
            }

            if (!allowedRoots.Contains(root))
            {
                reason =
                    $"Test file introduces '{root}' but existing tests in this repo do not use it. "
                    + $"Mirror exemplar *Tests.cs (allowed roots: {string.Join(", ", allowedRoots.OrderBy(r => r, StringComparer.Ordinal))}) "
                    + "or rely on workflow package restore after apply.";
                return false;
            }
        }

        return true;
    }

    internal static IReadOnlyList<string> EnsureMissingPackages(
        string repoPath,
        IReadOnlyList<AgentFinding>? buildFindings = null,
        IReadOnlyList<GeneratedFile>? proposedFiles = null)
    {
        var applied = new List<string>();
        var requiredPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var finding in buildFindings ?? Array.Empty<AgentFinding>())
        {
            foreach (Match match in Cs0246Regex.Matches(finding.Message))
            {
                TryMapRootToPackage(match.Groups[1].Value, requiredPackages);
            }
        }

        string? defaultTestCsproj = DiscoverPrimaryTestProjectPath(repoPath);
        foreach (string testCsproj in DiscoverTestProjectPaths(repoPath))
        {
            var referenced = ReadPackageIds(testCsproj);
            foreach (string path in EnumerateTestSourcesForProject(repoPath, testCsproj))
            {
                string content = File.ReadAllText(path);
                foreach (string root in ExtractUsingRoots(content))
                {
                    if (TryMapRootToPackage(root, out string? packageId)
                        && !referenced.Contains(packageId))
                    {
                        requiredPackages.Add(packageId);
                    }
                }
            }
        }

        if (proposedFiles is not null)
        {
            foreach (var file in proposedFiles.Where(f => IsTestSourcePath(f.RelativePath)))
            {
                string? csproj = ResolveProjectPathForSource(repoPath, file.RelativePath) ?? defaultTestCsproj;
                if (string.IsNullOrWhiteSpace(csproj))
                {
                    continue;
                }

                var referenced = ReadPackageIds(csproj);
                foreach (string root in ExtractUsingRoots(file.Content))
                {
                    if (TryMapRootToPackage(root, out string? packageId)
                        && !referenced.Contains(packageId))
                    {
                        requiredPackages.Add(packageId);
                    }
                }
            }
        }

        foreach (string packageId in requiredPackages.OrderBy(p => p, StringComparer.Ordinal))
        {
            string? targetCsproj = defaultTestCsproj ?? DiscoverPrimaryTestProjectPath(repoPath);
            if (string.IsNullOrWhiteSpace(targetCsproj))
            {
                continue;
            }

            if (ReadPackageIds(targetCsproj).Contains(packageId))
            {
                continue;
            }

            if (TryDotnetAddPackage(repoPath, targetCsproj, packageId, out string? error))
            {
                applied.Add($"added package {packageId} to {Path.GetRelativePath(repoPath, targetCsproj).Replace('\\', '/')}");
            }
            else if (!string.IsNullOrWhiteSpace(error))
            {
                applied.Add($"failed to add package {packageId}: {error}");
            }
        }

        return applied;
    }

    private static bool TryMapRootToPackage(string root, HashSet<string> packages)
    {
        if (TryMapRootToPackage(root, out string? packageId))
        {
            packages.Add(packageId);
            return true;
        }

        return false;
    }

    private static bool TryMapRootToPackage(string root, out string packageId)
    {
        if (RootNamespaceToPackageId.TryGetValue(root, out string? mapped) && !string.IsNullOrWhiteSpace(mapped))
        {
            packageId = mapped;
            return true;
        }

        packageId = string.Empty;
        return false;
    }

    private static HashSet<string> DiscoverTestPackageIds(string repoPath)
    {
        var packages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string csproj in DiscoverTestProjectPaths(repoPath))
        {
            foreach (string id in ReadPackageIds(csproj))
            {
                packages.Add(id);
            }
        }

        return packages;
    }

    private static HashSet<string> DiscoverAllowedTestRootNamespaces(string repoPath)
    {
        var roots = new HashSet<string>(StringComparer.Ordinal);
        foreach (string path in Directory.EnumerateFiles(repoPath, "*Tests.cs", SearchOption.AllDirectories))
        {
            if (path.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                || path.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (string root in ExtractUsingRoots(File.ReadAllText(path)))
            {
                if (IsRepoTestRoot(root))
                {
                    roots.Add(root);
                }
            }
        }

        return roots;
    }

    private static bool IsRepoTestRoot(string root) =>
        !root.StartsWith("System", StringComparison.Ordinal)
        && !root.StartsWith("Microsoft", StringComparison.Ordinal);

    private static IEnumerable<string> ExtractUsingRoots(string content)
    {
        foreach (Match match in UsingRegex.Matches(content))
        {
            string ns = match.Groups[1].Value;
            string root = ns.Contains('.') ? ns[..ns.IndexOf('.')] : ns;
            if (!string.IsNullOrWhiteSpace(root))
            {
                yield return root;
            }
        }
    }

    private static HashSet<string> ReadPackageIds(string csprojPath)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!TryParseCsprojDocument(csprojPath, out XDocument? doc) || doc is null)
        {
            return ids;
        }

        foreach (var element in doc.Descendants().Where(e => e.Name.LocalName == "PackageReference"))
        {
            string? include = element.Attribute("Include")?.Value;
            if (!string.IsNullOrWhiteSpace(include))
            {
                ids.Add(include);
            }
        }

        return ids;
    }

    /// <summary>
    /// Skips empty, non-XML, or agent-corrupted .csproj files so package audit does not abort apply.
    /// </summary>
    private static bool TryParseCsprojDocument(string csprojPath, out XDocument? document)
    {
        document = null;
        if (!File.Exists(csprojPath))
        {
            return false;
        }

        string content;
        try
        {
            content = File.ReadAllText(csprojPath);
        }
        catch
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        content = content.TrimStart('\uFEFF');
        int i = 0;
        while (i < content.Length && char.IsWhiteSpace(content[i]))
        {
            i++;
        }

        if (i >= content.Length || content[i] != '<')
        {
            return false;
        }

        try
        {
            document = XDocument.Parse(content, LoadOptions.None);
            return string.Equals(document.Root?.Name.LocalName, "Project", StringComparison.OrdinalIgnoreCase);
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private static bool IsParsableCsproj(string csprojPath) => TryParseCsprojDocument(csprojPath, out _);

    private static List<string> DiscoverTestProjectPaths(string repoPath) =>
        Directory
            .EnumerateFiles(repoPath, "*.csproj", SearchOption.AllDirectories)
            .Where(BuildFailureClassifier.IsTestProjectPath)
            .Where(path => !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
            .Where(IsParsableCsproj)
            .ToList();

    private static string? DiscoverPrimaryTestProjectPath(string repoPath) =>
        DiscoverTestProjectPaths(repoPath)
            .OrderBy(path => path.Contains("UnitTest", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(path => path.Length)
            .FirstOrDefault();

    private static string? ResolveProjectPathForSource(string repoPath, string relativePath)
    {
        string absoluteDir = Path.GetDirectoryName(Path.Combine(repoPath, relativePath.Replace('/', Path.DirectorySeparatorChar))) ?? repoPath;
        while (!string.IsNullOrWhiteSpace(absoluteDir) && absoluteDir.StartsWith(repoPath, StringComparison.Ordinal))
        {
            if (Directory.Exists(absoluteDir))
            {
                string? csproj = Directory
                    .GetFiles(absoluteDir, "*.csproj")
                    .Where(IsParsableCsproj)
                    .OrderByDescending(BuildFailureClassifier.IsTestProjectPath)
                    .FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(csproj))
                {
                    return csproj;
                }
            }

            absoluteDir = Path.GetDirectoryName(absoluteDir) ?? string.Empty;
        }

        return DiscoverPrimaryTestProjectPath(repoPath);
    }

    private static IEnumerable<string> EnumerateTestSourcesForProject(string repoPath, string csprojPath)
    {
        string projectDir = Path.GetDirectoryName(csprojPath) ?? repoPath;
        if (!Directory.Exists(projectDir))
        {
            yield break;
        }

        foreach (string file in Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories))
        {
            if (!file.Contains("/obj/", StringComparison.OrdinalIgnoreCase))
            {
                yield return file;
            }
        }
    }

    private static bool IsTestSourcePath(string relativePath) =>
        BuildFailureClassifier.IsTestArtifactPath(relativePath);

    private static bool TryDotnetAddPackage(
        string repoPath,
        string csprojPath,
        string packageId,
        out string? error)
    {
        error = null;
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"add \"{csprojPath}\" package {packageId}",
                WorkingDirectory = repoPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        try
        {
            process.Start();
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                error = string.IsNullOrWhiteSpace(stderr) ? stdout.Trim() : stderr.Trim();
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
