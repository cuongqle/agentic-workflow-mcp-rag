using System.Xml.Linq;

namespace workflowX.Infrastructure.Compliance.DotNet;

/// <summary>
/// Resolves test-project paths from on-disk .csproj markers (no folder-name heuristics).
/// </summary>
internal static class TestProjectPathSupport
{
    internal static bool IsTestSourcePath(string repoPath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)
            || !relativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string normalized = relativePath.Replace('\\', '/');
        if (normalized.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return TryResolveOwningTestCsproj(repoPath, relativePath) is not null;
    }

    internal static string? TryResolveOwningTestCsproj(string repoPath, string sourceRelativePath) =>
        TryResolveOwningCsproj(repoPath, sourceRelativePath, testProjectsOnly: true);

    internal static void ExpandWithOwningTestProjects(string repoPath, HashSet<string> paths)
    {
        foreach (string path in paths.ToList())
        {
            if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? csproj = TryResolveOwningTestCsproj(repoPath, path);
            if (!string.IsNullOrWhiteSpace(csproj))
            {
                paths.Add(csproj);
            }
        }
    }

    private static string? TryResolveOwningCsproj(
        string repoPath,
        string sourceRelativePath,
        bool testProjectsOnly)
    {
        if (string.IsNullOrWhiteSpace(repoPath) || string.IsNullOrWhiteSpace(sourceRelativePath))
        {
            return null;
        }

        string absoluteSource = Path.GetFullPath(Path.Combine(repoPath, sourceRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        string? directory = Path.GetDirectoryName(absoluteSource);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        string repoRoot = Path.GetFullPath(repoPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (int depth = 0; depth < 8 && directory.StartsWith(repoRoot, StringComparison.OrdinalIgnoreCase); depth++)
        {
            if (Directory.Exists(directory))
            {
                foreach (string csproj in Directory.EnumerateFiles(directory, "*.csproj", SearchOption.TopDirectoryOnly))
                {
                    if (testProjectsOnly && !IsTestProjectCsproj(csproj))
                    {
                        continue;
                    }

                    return Path.GetRelativePath(repoRoot, csproj).Replace('\\', '/');
                }
            }

            directory = Path.GetDirectoryName(directory);
            if (string.IsNullOrWhiteSpace(directory))
            {
                break;
            }
        }

        return null;
    }

    internal static bool IsTestProjectCsproj(string csprojAbsolute)
    {
        if (!File.Exists(csprojAbsolute))
        {
            return false;
        }

        try
        {
            XDocument document = XDocument.Load(csprojAbsolute);
            string? sdk = document.Root?.Attribute("Sdk")?.Value;
            if (!string.IsNullOrWhiteSpace(sdk)
                && sdk.Contains("Test", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            foreach (XElement property in document.Descendants("IsTestProject"))
            {
                if (string.Equals(property.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return document
                .Descendants("PackageReference")
                .Select(reference => reference.Attribute("Include")?.Value)
                .Any(IsTestFrameworkPackageReference);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsTestFrameworkPackageReference(string? packageId)
    {
        if (string.IsNullOrWhiteSpace(packageId))
        {
            return false;
        }

        return string.Equals(packageId, "Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase)
               || packageId.StartsWith("xunit", StringComparison.OrdinalIgnoreCase)
               || packageId.StartsWith("NUnit", StringComparison.OrdinalIgnoreCase)
               || packageId.StartsWith("MSTest.", StringComparison.OrdinalIgnoreCase);
    }
}
