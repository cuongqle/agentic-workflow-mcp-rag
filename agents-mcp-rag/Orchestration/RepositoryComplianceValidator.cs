using System.Text.RegularExpressions;
using agents_mcp_rag.Infrastructure;

static class RepositoryComplianceValidator
{
    public static List<AgentFinding> CollectComplianceFindings(WorkflowState state)
    {
        var findings = ValidateRepositoryContracts(state);
        findings.AddRange(ValidatePathConventions(state));
        findings.AddRange(TestCoverageAuditor.ValidateMissingTests(state));
        return findings;
    }

private static List<AgentFinding> ValidateRepositoryContracts(WorkflowState state)
{
    var findings = new List<AgentFinding>();
    var backendFiles = WorkflowFindingRules.GetAllProposedFiles(state);
    var proposedPaths = new HashSet<string>(backendFiles.Select(f => f.RelativePath.Replace('\\', '/')), StringComparer.OrdinalIgnoreCase);
    var interfaceConvention = DetectRepositoryInterfaceConvention(state.RepoPath);
    var layerProfiles = LayerConventionProfileBuilder.Build(state.RepoPath);
    var repositoryProfile = layerProfiles.Repository;

    foreach (var repoImplPath in proposedPaths.Where(IsRepositoryImplementationPath))
    {
        string fileName = Path.GetFileName(repoImplPath);
        string entity = fileName[..^"Repository.cs".Length];
        if (string.IsNullOrWhiteSpace(entity))
        {
            continue;
        }

        string expectedInterfaceName = $"I{entity}Repository.cs";
        string expectedImplementationName = $"{entity}Repository.cs";
        bool interfaceInProposed = proposedPaths.Any(path => Path.GetFileName(path).Equals(expectedInterfaceName, StringComparison.OrdinalIgnoreCase));
        string? interfaceFilePathInRepo = Directory
            .EnumerateFiles(state.RepoPath, expectedInterfaceName, SearchOption.AllDirectories)
            .FirstOrDefault(path => path.Contains("Interfaces", StringComparison.OrdinalIgnoreCase));
        bool interfaceInRepo = !string.IsNullOrWhiteSpace(interfaceFilePathInRepo);

        if (!interfaceInProposed && !interfaceInRepo)
        {
            findings.Add(new AgentFinding
            {
                Severity = FindingSeverity.High,
                Message = $"Missing repository interface for {entity}: expected {expectedInterfaceName} under Interfaces."
            });
            continue;
        }

        string? interfaceContent = backendFiles
            .FirstOrDefault(f => Path.GetFileName(f.RelativePath).Equals(expectedInterfaceName, StringComparison.OrdinalIgnoreCase))
            ?.Content;
        if (string.IsNullOrWhiteSpace(interfaceContent) && interfaceInRepo && !string.IsNullOrWhiteSpace(interfaceFilePathInRepo))
        {
            interfaceContent = File.ReadAllText(interfaceFilePathInRepo);
        }
        if (!string.IsNullOrWhiteSpace(interfaceContent))
        {
            if (interfaceConvention.RequireInheritanceClause && !interfaceContent.Contains(':'))
            {
                findings.Add(new AgentFinding
                {
                    Severity = FindingSeverity.High,
                    Message = $"Interface {expectedInterfaceName} should define an inheritance clause to match existing repository interface style."
                });
            }

            foreach (var token in interfaceConvention.RequiredBaseTokens)
            {
                if (!InterfaceContainsToken(interfaceContent, token))
                {
                    findings.Add(new AgentFinding
                    {
                        Severity = FindingSeverity.High,
                        Message = $"Interface {expectedInterfaceName} should include base token '{token}' to match repository contracts."
                    });
                }
            }
        }

        string? implementationContent = backendFiles
            .FirstOrDefault(f => Path.GetFileName(f.RelativePath).Equals(expectedImplementationName, StringComparison.OrdinalIgnoreCase))
            ?.Content;
        if (string.IsNullOrWhiteSpace(implementationContent))
        {
            string? implementationPathInRepo = Directory
                .EnumerateFiles(state.RepoPath, expectedImplementationName, SearchOption.AllDirectories)
                .FirstOrDefault(path => !path.Contains("/Interfaces/", StringComparison.OrdinalIgnoreCase)
                                     && path.Contains("Repository", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(implementationPathInRepo))
            {
                implementationContent = File.ReadAllText(implementationPathInRepo);
            }
        }

        if (!string.IsNullOrWhiteSpace(implementationContent))
        {
            if (implementationContent.Contains("// Implement", StringComparison.OrdinalIgnoreCase)
                || implementationContent.Contains("// TODO", StringComparison.OrdinalIgnoreCase)
                || implementationContent.Contains("throw new NotImplementedException", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new AgentFinding
                {
                    Severity = FindingSeverity.High,
                    Message = $"{expectedImplementationName} contains placeholder implementation markers."
                });
            }

            string? classLine = implementationContent
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .Select(line => line.Trim())
                .FirstOrDefault(line => line.StartsWith("public class ", StringComparison.Ordinal));

            if (repositoryProfile is not null && !string.IsNullOrWhiteSpace(classLine))
            {
                if (repositoryProfile.RequireInheritanceClause && !classLine.Contains(':'))
                {
                    findings.Add(new AgentFinding
                    {
                        Severity = FindingSeverity.High,
                        Message = $"{expectedImplementationName} should include an inheritance clause to match repository style."
                    });
                }

                if (repositoryProfile.RequireMatchingRoleInterface
                    && !classLine.Contains($"I{entity}Repository", StringComparison.Ordinal))
                {
                    findings.Add(new AgentFinding
                    {
                        Severity = FindingSeverity.High,
                        Message = $"{expectedImplementationName} should implement I{entity}Repository based on current conventions."
                    });
                }

                foreach (var token in repositoryProfile.RequiredInheritedTypeTokens)
                {
                    if (!ClassLineMatchesToken(classLine, token))
                    {
                        findings.Add(new AgentFinding
                        {
                            Severity = FindingSeverity.High,
                            Message = $"{expectedImplementationName} should include inherited token '{token}' based on repository conventions."
                        });
                    }
                }

                if (repositoryProfile.RequireBaseConstructorCall && !implementationContent.Contains("base(", StringComparison.Ordinal))
                {
                    findings.Add(new AgentFinding
                    {
                        Severity = FindingSeverity.High,
                        Message = $"{expectedImplementationName} should include constructor base(...) call to match repository conventions."
                    });
                }

                foreach (var paramType in repositoryProfile.RequiredConstructorParamTypes)
                {
                    if (!implementationContent.Contains(paramType, StringComparison.Ordinal))
                    {
                        findings.Add(new AgentFinding
                        {
                            Severity = FindingSeverity.High,
                            Message = $"{expectedImplementationName} should include constructor dependency '{paramType}' based on repository conventions."
                        });
                    }
                }
            }
        }
    }

    return findings;
}

private static List<AgentFinding> ValidatePathConventions(WorkflowState state)
{
    var findings = new List<AgentFinding>();
    var backendFiles = WorkflowFindingRules.GetAllProposedFiles(state);
    string? webApiControllersDir = DetectCanonicalWebApiControllersDir(state.RepoPath);
    string? repoIndexesDir = DetectCanonicalRepositoryIndexesDir(state.RepoPath);

    foreach (var file in backendFiles)
    {
        string path = file.RelativePath.Replace('\\', '/');
        string fileName = Path.GetFileName(path);
        if (fileName.EndsWith("Controller.cs", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(webApiControllersDir)
            && !path.StartsWith(webApiControllersDir + "/", StringComparison.OrdinalIgnoreCase)
            && !path.Equals(webApiControllersDir, StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new AgentFinding
            {
                Severity = FindingSeverity.High,
                Message = $"{fileName} should be generated under {webApiControllersDir}, not {path}."
            });
        }

        if (fileName.EndsWith("Index.cs", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(repoIndexesDir)
            && !path.StartsWith(repoIndexesDir + "/", StringComparison.OrdinalIgnoreCase)
            && !path.Equals(repoIndexesDir, StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new AgentFinding
            {
                Severity = FindingSeverity.High,
                Message = $"{fileName} should be generated under {repoIndexesDir}, not {path}."
            });
        }
    }

    var allIndexFiles = Directory
        .EnumerateFiles(state.RepoPath, "*Index.cs", SearchOption.AllDirectories)
        .Where(path => !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                    && !path.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
        .ToList();
    var duplicatedIndexNames = allIndexFiles
        .GroupBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
        .Where(group => group.Select(Path.GetDirectoryName).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
        .Select(group => group.Key)
        .ToList();
    foreach (var duplicated in duplicatedIndexNames)
    {
        findings.Add(new AgentFinding
        {
            Severity = FindingSeverity.High,
            Message = $"Duplicate index file detected in multiple folders: {duplicated}. Keep only canonical index path."
        });
    }

    return findings;
}

private static bool IsRepositoryImplementationPath(string path)
{
    string fileName = Path.GetFileName(path);
    return fileName.EndsWith("Repository.cs", StringComparison.OrdinalIgnoreCase)
           && !fileName.StartsWith("I", StringComparison.OrdinalIgnoreCase);
}

private static List<GeneratedFile> GetAllProposedFiles(WorkflowState state)
{
    return (state.Backend?.ProposedFiles ?? new List<GeneratedFile>())
        .Concat(state.Recovery?.ProposedFiles ?? new List<GeneratedFile>())
        .ToList();
}

private static bool HasBlockingFindings(IEnumerable<AgentFinding> findings)
{
    return findings.Any(f => f.Severity is FindingSeverity.High or FindingSeverity.Blocker);
}

private static RepositoryInterfaceConvention DetectRepositoryInterfaceConvention(string repoPath)
{
    var interfaceFiles = Directory
        .EnumerateFiles(repoPath, "I*Repository.cs", SearchOption.AllDirectories)
        .Where(path => path.Contains("Interfaces", StringComparison.OrdinalIgnoreCase))
        .ToList();
    if (interfaceFiles.Count == 0)
    {
        return new RepositoryInterfaceConvention(
            RequireInheritanceClause: true,
            RequiredBaseTokens: Array.Empty<string>());
    }

    int withAnyBaseClause = 0;
    var baseTokenCounts = new Dictionary<string, int>(StringComparer.Ordinal);
    foreach (var file in interfaceFiles)
    {
        string? declaration = File.ReadLines(file)
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith("public interface ", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(declaration))
        {
            continue;
        }

        if (declaration.Contains(':'))
        {
            withAnyBaseClause++;
            string inheritance = declaration.Split(':', 2)[1];
            foreach (var token in inheritance.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => NormalizeGenericToken(x.Trim())))
            {
                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }
                baseTokenCounts[token] = baseTokenCounts.TryGetValue(token, out int count) ? count + 1 : 1;
            }
        }
    }

    int threshold = Math.Max(2, (int)Math.Ceiling(interfaceFiles.Count * 0.6));
    var requiredTokens = baseTokenCounts
        .Where(kvp => kvp.Value >= threshold)
        .Select(kvp => kvp.Key)
        .OrderBy(x => x, StringComparer.Ordinal)
        .ToList();

    return new RepositoryInterfaceConvention(
        RequireInheritanceClause: withAnyBaseClause > 0,
        RequiredBaseTokens: requiredTokens);
}

private static string? DetectCanonicalWebApiControllersDir(string repoPath)
{
    return DetectCanonicalDirectoryForFileSuffix(repoPath, "Controller.cs", "Controllers");
}

private static string? DetectCanonicalRepositoryIndexesDir(string repoPath)
{
    return DetectCanonicalDirectoryForFileSuffix(repoPath, "Index.cs", "Indexes")
           ?? DetectCanonicalDirectoryForFileSuffix(repoPath, "Index.cs", "Index");
}

private static string? DetectCanonicalDirectoryForFileSuffix(
    string repoPath,
    string fileSuffix,
    string? preferredDirectoryName = null)
{
    var matchingFiles = Directory.EnumerateFiles(repoPath, $"*{fileSuffix}", SearchOption.AllDirectories)
        .Where(path => !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                    && !path.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
        .ToList();
    if (matchingFiles.Count == 0)
    {
        return null;
    }

    var grouped = matchingFiles
        .Select(path => Path.GetRelativePath(repoPath, Path.GetDirectoryName(path) ?? string.Empty).Replace('\\', '/'))
        .Where(relative => !string.IsNullOrWhiteSpace(relative))
        .GroupBy(relative => relative, StringComparer.OrdinalIgnoreCase)
        .Select(group => new
        {
            Directory = group.Key,
            Count = group.Count(),
            IsPreferred = !string.IsNullOrWhiteSpace(preferredDirectoryName)
                          && group.Key.EndsWith("/" + preferredDirectoryName, StringComparison.OrdinalIgnoreCase)
                          || (!string.IsNullOrWhiteSpace(preferredDirectoryName)
                              && group.Key.Equals(preferredDirectoryName, StringComparison.OrdinalIgnoreCase))
        })
        .OrderByDescending(entry => entry.Count)
        .ThenByDescending(entry => entry.IsPreferred)
        .ThenBy(entry => entry.Directory.Length)
        .ThenBy(entry => entry.Directory, StringComparer.OrdinalIgnoreCase)
        .ToList();

    return grouped.FirstOrDefault()?.Directory;
}

private static bool InterfaceContainsToken(string interfaceContent, string normalizedToken)
{
    if (string.IsNullOrWhiteSpace(normalizedToken))
    {
        return true;
    }

    string normalizedContent = NormalizeGenericToken(interfaceContent);
    return normalizedContent.Contains(normalizedToken, StringComparison.Ordinal);
}

private static bool ClassLineMatchesToken(string classLine, string token)
{
    if (string.IsNullOrWhiteSpace(token))
    {
        return true;
    }

    string normalizedLine = NormalizeGenericToken(classLine);
    return normalizedLine.Contains(token, StringComparison.Ordinal);
}

private static string NormalizeGenericToken(string value)
{
    // Replace concrete generic arguments with <T> so conventions remain generic.
    return Regex.Replace(value, @"<\s*[A-Za-z_][A-Za-z0-9_]*\s*>", "<T>");
}

private readonly record struct RepositoryInterfaceConvention(
    bool RequireInheritanceClause,
    IReadOnlyList<string> RequiredBaseTokens);
}
