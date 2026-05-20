using System.Text.RegularExpressions;

namespace agents_mcp_rag.Infrastructure;

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
                    if (finding.Message.Contains("Build FAILED", StringComparison.OrdinalIgnoreCase)
                        || finding.Message.Contains("Build failed", StringComparison.OrdinalIgnoreCase))
                    {
                        unscoped.Add(finding);
                    }
                    else
                    {
                        unscoped.Add(finding);
                    }
                    break;
            }
        }

        if (unscoped.Count > 0 && production.Count == 0 && test.Count > 0)
        {
            foreach (var finding in unscoped.ToList())
            {
                if (finding.Message.Contains("Build FAILED", StringComparison.OrdinalIgnoreCase)
                    || finding.Message.Contains("Build failed", StringComparison.OrdinalIgnoreCase))
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
