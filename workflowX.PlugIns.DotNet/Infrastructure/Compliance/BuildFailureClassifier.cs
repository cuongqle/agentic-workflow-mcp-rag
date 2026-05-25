using System.Text.RegularExpressions;

namespace workflowX.Infrastructure.Compliance.DotNet;

internal enum BuildFailureScope
{
    Unknown,
    Production,
    Test
}

internal readonly record struct BuildFailureAnalysis(
    IReadOnlyList<AgentFinding> ProductionFailures,
    IReadOnlyList<AgentFinding> TestFailures,
    IReadOnlyList<AgentFinding> UnscopedFailures)
{
    public bool HasProductionFailures => ProductionFailures.Count > 0 || UnscopedFailures.Count > 0;

    public bool HasTestFailures => TestFailures.Count > 0;

    public bool IsTestOnly =>
        !HasProductionFailures && HasTestFailures;
}

internal static class BuildFailureClassifier
{
    /// <summary>dotnet build/msbuild summary lines — not actionable compiler diagnostics.</summary>
    public static bool IsSummaryBanner(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return true;
        }

        return message.Contains("Build FAILED", StringComparison.OrdinalIgnoreCase)
               || message.Contains("Build failed", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsActionableFinding(AgentFinding finding) =>
        finding.Severity is FindingSeverity.High or FindingSeverity.Blocker
        && !IsSummaryBanner(finding.Message);

    public static BuildFailureAnalysis Analyze(IReadOnlyList<AgentFinding> findings)
    {
        var production = new List<AgentFinding>();
        var test = new List<AgentFinding>();
        var unscoped = new List<AgentFinding>();

        foreach (var finding in findings)
        {
            switch (ClassifyMessage(finding.Message))
            {
                case BuildFailureScope.Production:
                    production.Add(finding);
                    break;
                case BuildFailureScope.Test:
                    test.Add(finding);
                    break;
                default:
                    unscoped.Add(finding);
                    break;
            }
        }

        if (unscoped.Count > 0 && production.Count == 0 && test.Count > 0)
        {
            foreach (var finding in unscoped.ToList())
            {
                if (IsSummaryBanner(finding.Message))
                {
                    unscoped.Remove(finding);
                    test.Add(finding);
                }
            }
        }

        return new BuildFailureAnalysis(production, test, unscoped);
    }

    public static bool IsOnlyTestFailures(IReadOnlyList<AgentFinding> findings)
    {
        return Analyze(findings).IsTestOnly;
    }

    public static bool IsTestArtifactPath(string relativePath)
    {
        string normalized = relativePath.Replace('\\', '/');
        return normalized.Contains(".UnitTest/", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("/UnitTest/", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("/RepositoryTest/", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("/Tests/", StringComparison.OrdinalIgnoreCase)
               || Path.GetFileName(normalized).EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase)
               || Path.GetFileName(normalized).EndsWith(".Tests.csproj", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsTestProjectPath(string projectPath)
    {
        string normalized = projectPath.Replace('\\', '/');
        string fileName = Path.GetFileName(normalized);
        return fileName.Contains("Test", StringComparison.OrdinalIgnoreCase)
               || fileName.Contains(".Tests.", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains(".UnitTest/", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("/UnitTest/", StringComparison.OrdinalIgnoreCase);
    }

    internal static IEnumerable<string> ExtractTypeSymbolsFromMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            yield break;
        }

        var symbols = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(
                     message,
                     @"'([A-Za-z_][A-Za-z0-9_]*)'\s+does not contain a definition for",
                     RegexOptions.IgnoreCase))
        {
            symbols.Add(match.Groups[1].Value);
        }

        foreach (Match match in Regex.Matches(
                     message,
                     @"type or namespace name\s+'([A-Za-z_][A-Za-z0-9_]*)'",
                     RegexOptions.IgnoreCase))
        {
            symbols.Add(match.Groups[1].Value);
        }

        foreach (Match match in Regex.Matches(
                     message,
                     @"name '([A-Za-z_][A-Za-z0-9_]*)' could not be found",
                     RegexOptions.IgnoreCase))
        {
            symbols.Add(match.Groups[1].Value);
        }

        foreach (Match match in Regex.Matches(
                     message,
                     @"'([A-Za-z_][A-Za-z0-9_]*)'\s+does not implement",
                     RegexOptions.IgnoreCase))
        {
            symbols.Add(match.Groups[1].Value);
        }

        foreach (string typeName in ExtractTypeNamesFromQuotedSegments(message))
        {
            symbols.Add(typeName);
        }

        foreach (string symbol in symbols)
        {
            yield return symbol;
        }
    }

    internal static HashSet<string> CollectSourcePathsFromFindings(
        IReadOnlyList<AgentFinding> findings,
        string repoPath)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var finding in findings)
        {
            foreach (string path in CollectSourcePathsFromMessage(finding.Message, repoPath))
            {
                paths.Add(path);
            }
        }

        return paths;
    }

    internal static IEnumerable<string> CollectSourcePathsFromMessage(string message, string repoPath)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            yield break;
        }

        foreach (Match match in Regex.Matches(
                     message,
                     @"([A-Za-z0-9_\-./\\]+\.(?:cs|csproj))(?:\(\d+,\d+\))?",
                     RegexOptions.IgnoreCase))
        {
            string? relative = NormalizeToRepoRelativePath(match.Groups[1].Value, repoPath);
            if (!string.IsNullOrWhiteSpace(relative)
                && !relative.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                && !relative.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
            {
                yield return relative;
            }
        }
    }

    internal static void ExpandDeclarationPathsForTypes(
        string repoPath,
        IEnumerable<string> typeNames,
        HashSet<string> targetPaths,
        IReadOnlyDictionary<string, List<string>> declarationIndex)
    {
        foreach (string typeName in typeNames.Distinct(StringComparer.Ordinal))
        {
            string lookup = StripGenericArity(typeName);
            if (declarationIndex.TryGetValue(lookup, out List<string>? indexed))
            {
                foreach (string path in indexed)
                {
                    targetPaths.Add(path);
                }
            }

            foreach (string absolute in Directory.EnumerateFiles(repoPath, $"{lookup}.cs", SearchOption.AllDirectories))
            {
                if (absolute.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                    || absolute.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                targetPaths.Add(Path.GetRelativePath(repoPath, absolute).Replace('\\', '/'));
            }
        }
    }

    internal static string StripGenericArity(string typeName)
    {
        int genericStart = typeName.IndexOf('<');
        return genericStart > 0 ? typeName[..genericStart] : typeName;
    }

    private static IEnumerable<string> ExtractTypeNamesFromQuotedSegments(string message)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(message, @"'([^']+)'", RegexOptions.IgnoreCase))
        {
            string quoted = match.Groups[1].Value.Trim();
            if (!IsLikelyTypeToken(quoted))
            {
                continue;
            }

            string typePart = quoted.Contains('.') ? quoted[..quoted.IndexOf('.')] : quoted;
            AddTypeLookupName(names, typePart);
        }

        return names;
    }

    private static void AddTypeLookupName(HashSet<string> names, string typePart)
    {
        names.Add(StripGenericArity(typePart));
        int namespaceDot = typePart.LastIndexOf('.');
        if (namespaceDot >= 0 && namespaceDot < typePart.Length - 1)
        {
            names.Add(StripGenericArity(typePart[(namespaceDot + 1)..]));
        }
    }

    private static bool IsLikelyTypeToken(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= 120
        && Regex.IsMatch(value, @"^[A-Za-z_][A-Za-z0-9_<>.,]*(\(\))?$");

    private static string? NormalizeToRepoRelativePath(string path, string repoPath)
    {
        string normalized = path.Replace('\\', '/');
        if (!Path.IsPathRooted(normalized))
        {
            return normalized.TrimStart('/');
        }

        string repoRoot = Path.GetFullPath(repoPath).Replace('\\', '/').TrimEnd('/');
        string absolute = Path.GetFullPath(path).Replace('\\', '/');
        return absolute.StartsWith(repoRoot + "/", StringComparison.OrdinalIgnoreCase)
            ? absolute[(repoRoot.Length + 1)..]
            : null;
    }

    internal static BuildFailureScope ClassifyMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return BuildFailureScope.Unknown;
        }

        foreach (Match match in Regex.Matches(message, @"([A-Za-z0-9_\-./\\]+\.(?:cs|csproj))(?:\(\d+,\d+\))?", RegexOptions.IgnoreCase))
        {
            string path = match.Groups[1].Value.Replace('\\', '/');
            if (IsTestArtifactPath(path) || IsTestProjectPath(path))
            {
                return BuildFailureScope.Test;
            }

            if (path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                return BuildFailureScope.Production;
            }

            if (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                return BuildFailureScope.Production;
            }
        }

        return BuildFailureScope.Unknown;
    }
}
