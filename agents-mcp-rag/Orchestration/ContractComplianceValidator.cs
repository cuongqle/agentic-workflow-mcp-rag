using System.Text.RegularExpressions;
using agents_mcp_rag.Infrastructure;

static class ContractComplianceValidator
{
    public static List<AgentFinding> CollectComplianceFindings(WorkflowState state)
    {
        var findings = ValidateLayerContracts(state);
        findings.AddRange(ValidatePathConventions(state));
        findings.AddRange(TestCoverageAuditor.ValidateMissingTests(state));
        findings.AddRange(DependencyWiringAuditor.ValidateMissingWiring(state));
        findings.AddRange(ValidateProtectedContractTampering(state));
        findings.AddRange(ValidateTestBootstrapResolution(state));
        return findings;
    }

    private static List<AgentFinding> ValidateLayerContracts(WorkflowState state)
    {
        var findings = new List<AgentFinding>();
        var proposedFiles = WorkflowFindingRules.GetAllProposedFiles(state);
        var proposedPaths = new HashSet<string>(
            proposedFiles.Select(f => f.RelativePath.Replace('\\', '/')),
            StringComparer.OrdinalIgnoreCase);
        var layerProfiles = LayerConventionProfileBuilder.Build(state.RepoPath);

        foreach (var profile in layerProfiles.GetActiveProfiles())
        {
            var interfaceConvention = DetectInterfaceConvention(state.RepoPath, profile);
            foreach (string implPath in proposedPaths.Where(path => MatchesProfileImplementation(path, profile)))
            {
                ValidateImplementationPair(
                    state,
                    profile,
                    interfaceConvention,
                    proposedFiles,
                    proposedPaths,
                    implPath,
                    findings);
            }
        }

        return findings;
    }

    private static void ValidateImplementationPair(
        WorkflowState state,
        LayerConventionProfile profile,
        LayerInterfaceConvention interfaceConvention,
        IReadOnlyList<GeneratedFile> proposedFiles,
        HashSet<string> proposedPaths,
        string implPath,
        List<AgentFinding> findings)
    {
        string fileName = Path.GetFileName(implPath);
        string? subjectBase = LayerConventionProfiles.GetSubjectBaseName(fileName, profile);
        if (string.IsNullOrWhiteSpace(subjectBase))
        {
            return;
        }

        string expectedInterfaceName = LayerConventionProfiles.BuildExpectedInterfaceFileName(subjectBase, profile);
        string expectedImplementationName = LayerConventionProfiles.BuildExpectedImplementationFileName(subjectBase, profile);
        string layerLabel = profile.RoleName.ToLowerInvariant();

        bool interfaceInProposed = proposedPaths.Any(path =>
            Path.GetFileName(path).Equals(expectedInterfaceName, StringComparison.OrdinalIgnoreCase));
        string? interfaceFilePathInRepo = FindInterfaceFile(state.RepoPath, expectedInterfaceName, profile);
        bool interfaceInRepo = !string.IsNullOrWhiteSpace(interfaceFilePathInRepo);

        if (interfaceConvention.LayerUsesInterfaces && !interfaceInProposed && !interfaceInRepo)
        {
            findings.Add(new AgentFinding
            {
                Severity = FindingSeverity.High,
                Message =
                    $"Missing {layerLabel} interface for {subjectBase}: expected {expectedInterfaceName} following existing I*{profile.RoleName} conventions."
            });
        }

        string? interfaceContent = proposedFiles
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
                    Message =
                        $"Interface {expectedInterfaceName} should define an inheritance clause to match existing {layerLabel} interface style."
                });
            }

            foreach (string token in interfaceConvention.RequiredBaseTokens)
            {
                if (!InterfaceContainsToken(interfaceContent, token))
                {
                    findings.Add(new AgentFinding
                    {
                        Severity = FindingSeverity.High,
                        Message =
                            $"Interface {expectedInterfaceName} should include base token '{token}' to match {layerLabel} contracts."
                    });
                }
            }
        }

        string? implementationContent = proposedFiles
            .FirstOrDefault(f => Path.GetFileName(f.RelativePath).Equals(expectedImplementationName, StringComparison.OrdinalIgnoreCase))
            ?.Content;
        if (string.IsNullOrWhiteSpace(implementationContent))
        {
            string? implementationPathInRepo = Directory
                .EnumerateFiles(state.RepoPath, expectedImplementationName, SearchOption.AllDirectories)
                .FirstOrDefault(path => LayerConventionProfiles.MatchesImplementationFile(Path.GetFileName(path), profile)
                                     && !path.Contains("/Interfaces/", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(implementationPathInRepo))
            {
                implementationContent = File.ReadAllText(implementationPathInRepo);
            }
        }

        if (string.IsNullOrWhiteSpace(implementationContent))
        {
            return;
        }

        if (ContainsPlaceholderMarkers(implementationContent))
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

        if (string.IsNullOrWhiteSpace(classLine))
        {
            return;
        }

        if (profile.RequireInheritanceClause && !classLine.Contains(':'))
        {
            findings.Add(new AgentFinding
            {
                Severity = FindingSeverity.High,
                Message = $"{expectedImplementationName} should include an inheritance clause to match {layerLabel} style."
            });
        }

        if (profile.RequireMatchingRoleInterface
            && !classLine.Contains($"I{subjectBase}{profile.RoleName}", StringComparison.Ordinal))
        {
            findings.Add(new AgentFinding
            {
                Severity = FindingSeverity.High,
                Message =
                    $"{expectedImplementationName} should implement I{subjectBase}{profile.RoleName} based on current {layerLabel} conventions."
            });
        }

        foreach (string token in profile.RequiredInheritedTypeTokens)
        {
            if (!ClassLineMatchesToken(classLine, token))
            {
                findings.Add(new AgentFinding
                {
                    Severity = FindingSeverity.High,
                    Message =
                        $"{expectedImplementationName} should include inherited token '{token}' based on {layerLabel} conventions."
                });
            }
        }

        if (profile.RequireBaseConstructorCall && !implementationContent.Contains("base(", StringComparison.Ordinal))
        {
            findings.Add(new AgentFinding
            {
                Severity = FindingSeverity.High,
                Message =
                    $"{expectedImplementationName} should include constructor base(...) call to match {layerLabel} conventions."
            });
        }

        foreach (string paramType in profile.RequiredConstructorParamTypes)
        {
            if (!implementationContent.Contains(paramType, StringComparison.Ordinal))
            {
                findings.Add(new AgentFinding
                {
                    Severity = FindingSeverity.High,
                    Message =
                        $"{expectedImplementationName} should include constructor dependency '{paramType}' based on {layerLabel} conventions."
                });
            }
        }
    }

    private static List<AgentFinding> ValidateTestBootstrapResolution(WorkflowState state)
    {
        var findings = new List<AgentFinding>();
        foreach (var file in WorkflowFindingRules.GetAllProposedFiles(state))
        {
            if (!file.RelativePath.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!TestBootstrapContext.TryValidateTestResolution(file.Content, state.RepoPath, out string reason))
            {
                findings.Add(new AgentFinding
                {
                    Severity = FindingSeverity.High,
                    Message = reason
                });
            }
        }

        return findings;
    }

    private static List<AgentFinding> ValidateProtectedContractTampering(WorkflowState state)
    {
        var findings = new List<AgentFinding>();
        var proposedPaths = new HashSet<string>(
            WorkflowFindingRules.GetAllProposedFiles(state).Select(f => f.RelativePath.Replace('\\', '/')),
            StringComparer.OrdinalIgnoreCase);

        foreach (var file in WorkflowFindingRules.GetAllProposedFiles(state))
        {
            string relative = file.RelativePath.Replace('\\', '/');
            string absolute = Path.Combine(state.RepoPath, relative.Replace('/', Path.DirectorySeparatorChar));
            string? existing = File.Exists(absolute) ? File.ReadAllText(absolute) : null;

            if (!PreExistingContractGuard.TryValidateOverwrite(relative, existing, file.Content, proposedPaths, out string reason))
            {
                findings.Add(new AgentFinding
                {
                    Severity = FindingSeverity.Blocker,
                    Message = reason
                });
            }
        }

        return findings;
    }

    private static List<AgentFinding> ValidatePathConventions(WorkflowState state)
    {
        var findings = new List<AgentFinding>();
        var proposedFiles = WorkflowFindingRules.GetAllProposedFiles(state);
        var layerProfiles = LayerConventionProfileBuilder.Build(state.RepoPath);

        foreach (var profile in layerProfiles.GetActiveProfiles())
        {
            string? canonicalDir = DetectCanonicalDirectoryForFileSuffix(
                state.RepoPath,
                profile.FileSuffix,
                InferPreferredDirectoryName(profile.RoleName));

            foreach (var file in proposedFiles)
            {
                string path = file.RelativePath.Replace('\\', '/');
                string fileName = Path.GetFileName(path);
                if (!LayerConventionProfiles.MatchesImplementationFile(fileName, profile))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(canonicalDir)
                    && !path.StartsWith(canonicalDir + "/", StringComparison.OrdinalIgnoreCase)
                    && !path.Equals(canonicalDir, StringComparison.OrdinalIgnoreCase))
                {
                    findings.Add(new AgentFinding
                    {
                        Severity = FindingSeverity.High,
                        Message = $"{fileName} should be generated under {canonicalDir}, not {path}."
                    });
                }
            }
        }

        string? repoIndexesDir = DetectCanonicalDirectoryForFileSuffix(state.RepoPath, "Index.cs", "Indexes")
                                 ?? DetectCanonicalDirectoryForFileSuffix(state.RepoPath, "Index.cs", "Index");
        foreach (var file in proposedFiles)
        {
            string path = file.RelativePath.Replace('\\', '/');
            string fileName = Path.GetFileName(path);
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

        var duplicatedIndexNames = Directory
            .EnumerateFiles(state.RepoPath, "*Index.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                        && !path.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
            .GroupBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(Path.GetDirectoryName).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .Select(group => group.Key);

        foreach (string duplicated in duplicatedIndexNames)
        {
            findings.Add(new AgentFinding
            {
                Severity = FindingSeverity.High,
                Message = $"Duplicate index file detected in multiple folders: {duplicated}. Keep only canonical index path."
            });
        }

        return findings;
    }

    private static bool MatchesProfileImplementation(string path, LayerConventionProfile profile) =>
        LayerConventionProfiles.MatchesImplementationFile(Path.GetFileName(path), profile);

    private static string? FindInterfaceFile(string repoPath, string interfaceFileName, LayerConventionProfile profile)
    {
        return Directory
            .EnumerateFiles(repoPath, interfaceFileName, SearchOption.AllDirectories)
            .FirstOrDefault(path =>
                path.Contains("Interfaces", StringComparison.OrdinalIgnoreCase)
                || path.Contains(profile.RoleName, StringComparison.OrdinalIgnoreCase));
    }

    private static LayerInterfaceConvention DetectInterfaceConvention(string repoPath, LayerConventionProfile profile)
    {
        string interfaceGlob = $"I*{profile.FileSuffix}";
        var interfaceFiles = Directory
            .EnumerateFiles(repoPath, interfaceGlob, SearchOption.AllDirectories)
            .Where(path => !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                        && !path.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (interfaceFiles.Count == 0)
        {
            return new LayerInterfaceConvention(
                LayerUsesInterfaces: false,
                RequireInheritanceClause: false,
                RequiredBaseTokens: Array.Empty<string>());
        }

        int withAnyBaseClause = 0;
        var baseTokenCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string file in interfaceFiles)
        {
            string? declaration = File.ReadLines(file)
                .Select(line => line.Trim())
                .FirstOrDefault(line => line.StartsWith("public interface ", StringComparison.Ordinal));
            if (string.IsNullOrWhiteSpace(declaration))
            {
                continue;
            }

            if (!declaration.Contains(':'))
            {
                continue;
            }

            withAnyBaseClause++;
            string inheritance = declaration.Split(':', 2)[1];
            foreach (string token in inheritance.Split(',', StringSplitOptions.RemoveEmptyEntries)
                         .Select(x => NormalizeGenericToken(x.Trim())))
            {
                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                baseTokenCounts[token] = baseTokenCounts.TryGetValue(token, out int count) ? count + 1 : 1;
            }
        }

        int threshold = Math.Max(2, (int)Math.Ceiling(interfaceFiles.Count * 0.6));
        var requiredTokens = baseTokenCounts
            .Where(kvp => kvp.Value >= threshold)
            .Select(kvp => kvp.Key)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        return new LayerInterfaceConvention(
            LayerUsesInterfaces: true,
            RequireInheritanceClause: withAnyBaseClause > 0,
            RequiredBaseTokens: requiredTokens);
    }

    private static string InferPreferredDirectoryName(string roleName) =>
        roleName switch
        {
            "Repository" => "Repository",
            "Controller" => "Controllers",
            "Service" => "Services",
            _ => roleName + "s"
        };

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

        return matchingFiles
            .Select(path => Path.GetRelativePath(repoPath, Path.GetDirectoryName(path) ?? string.Empty).Replace('\\', '/'))
            .Where(relative => !string.IsNullOrWhiteSpace(relative))
            .GroupBy(relative => relative, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Directory = group.Key,
                Count = group.Count(),
                IsPreferred = !string.IsNullOrWhiteSpace(preferredDirectoryName)
                              && (group.Key.EndsWith("/" + preferredDirectoryName, StringComparison.OrdinalIgnoreCase)
                                  || group.Key.Equals(preferredDirectoryName, StringComparison.OrdinalIgnoreCase))
            })
            .OrderByDescending(entry => entry.Count)
            .ThenByDescending(entry => entry.IsPreferred)
            .ThenBy(entry => entry.Directory.Length)
            .ThenBy(entry => entry.Directory, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()
            ?.Directory;
    }

    private static bool ContainsPlaceholderMarkers(string content) =>
        content.Contains("// Implement", StringComparison.OrdinalIgnoreCase)
        || content.Contains("// TODO", StringComparison.OrdinalIgnoreCase)
        || content.Contains("throw new NotImplementedException", StringComparison.OrdinalIgnoreCase);

    private static bool InterfaceContainsToken(string interfaceContent, string normalizedToken)
    {
        if (string.IsNullOrWhiteSpace(normalizedToken))
        {
            return true;
        }

        return NormalizeGenericToken(interfaceContent).Contains(normalizedToken, StringComparison.Ordinal);
    }

    private static bool ClassLineMatchesToken(string classLine, string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return true;
        }

        return NormalizeGenericToken(classLine).Contains(token, StringComparison.Ordinal);
    }

    private static string NormalizeGenericToken(string value) =>
        Regex.Replace(value, @"<\s*[A-Za-z_][A-Za-z0-9_]*\s*>", "<T>");

    private readonly record struct LayerInterfaceConvention(
        bool LayerUsesInterfaces,
        bool RequireInheritanceClause,
        IReadOnlyList<string> RequiredBaseTokens);
}
