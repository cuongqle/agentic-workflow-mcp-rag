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
        findings.AddRange(ValidateTypeMemberConsistency(state));
        findings.AddRange(ValidateInterfaceImplementation(state));
        findings.AddRange(ValidateTestPackageConventions(state));
        findings.AddRange(ValidateArchitectureCoverage(state));
        return findings;
    }

    private static List<AgentFinding> ValidateLayerContracts(WorkflowState state)
    {
        var findings = new List<AgentFinding>();
        var proposedFiles = WorkflowFindingRules.GetAllProposedFiles(state);
        var proposedPaths = new HashSet<string>(
            proposedFiles.Select(f => f.RelativePath.Replace('\\', '/')),
            StringComparer.OrdinalIgnoreCase);
        RepoContract contract = state.Contract ?? RepoContractDiscoverer.Discover(state.RepoPath);

        foreach (var profile in contract.LayerConventions.GetActiveProfiles())
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

        if (PlaceholderImplementationGuard.ContainsPlaceholderMarkers(implementationContent))
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

        string? ctorExemplarPath = LayerConventionProfiles.TryGetConstructorExemplarRelativePath(
            state.RepoPath,
            subjectBase,
            profile,
            implPath);
        foreach (string paramType in LayerConventionProfiles.ResolveRequiredConstructorParamTypes(
                     state.RepoPath,
                     subjectBase,
                     profile,
                     implPath))
        {
            if (!implementationContent.Contains(paramType, StringComparison.Ordinal))
            {
                string exemplarHint = string.IsNullOrWhiteSpace(ctorExemplarPath)
                    ? string.Empty
                    : $" Mirror {ctorExemplarPath}.";
                findings.Add(new AgentFinding
                {
                    Severity = FindingSeverity.High,
                    Message =
                        $"{expectedImplementationName} should include constructor dependency '{paramType}' based on {layerLabel} conventions.{exemplarHint}"
                });
            }
        }
    }

    private static List<AgentFinding> ValidateInterfaceImplementation(WorkflowState state)
    {
        var findings = new List<AgentFinding>();
        var proposed = WorkflowFindingRules.GetAllProposedFiles(state);
        var catalog = InterfaceImplementationGuard.BuildDirectMemberCatalog(state.RepoPath, proposed);

        foreach (var file in proposed)
        {
            if (!file.RelativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string fileName = Path.GetFileName(file.RelativePath);
            if (fileName.StartsWith('I') || !file.Content.Contains("public class ", StringComparison.Ordinal))
            {
                continue;
            }

            if (!InterfaceImplementationGuard.TryValidate(
                    state.RepoPath,
                    file.RelativePath.Replace('\\', '/'),
                    file.Content,
                    catalog,
                    out string reason))
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

    private static List<AgentFinding> ValidateTestPackageConventions(WorkflowState state)
    {
        var findings = new List<AgentFinding>();
        foreach (var file in WorkflowFindingRules.GetAllProposedFiles(state))
        {
            if (!ProjectPackageAuditor.TryValidateTestPackages(
                    state.RepoPath,
                    file.RelativePath.Replace('\\', '/'),
                    file.Content,
                    out string reason))
            {
                findings.Add(new AgentFinding
                {
                    Severity = FindingSeverity.Medium,
                    Message = reason
                });
            }
        }

        return findings;
    }

    private static List<AgentFinding> ValidateTypeMemberConsistency(WorkflowState state)
    {
        var findings = new List<AgentFinding>();
        var proposed = WorkflowFindingRules.GetAllProposedFiles(state);
        var proposedDefinitions = TypeMemberConsistencyGuard.BuildProposedTypeDefinitions(proposed);

        foreach (var file in proposed)
        {
            if (!TypeMemberConsistencyGuard.IsConsumerRelativePath(
                    state.RepoPath,
                    file.RelativePath.Replace('\\', '/'),
                    proposedDefinitions))
            {
                continue;
            }

            if (!TypeMemberConsistencyGuard.TryValidateConsumerContent(
                    state.RepoPath,
                    file.RelativePath.Replace('\\', '/'),
                    file.Content,
                    proposedDefinitions,
                    out string reason))
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
                continue;
            }

            if (!TestBootstrapContext.TryValidateTestLiteralTypes(file.Content, state.RepoPath, out string literalReason))
            {
                findings.Add(new AgentFinding
                {
                    Severity = FindingSeverity.High,
                    Message = literalReason
                });
                continue;
            }

            string? productionBase = TestCoverageAuditor.ExtractProductionBaseNameFromTestFileName(
                Path.GetFileName(file.RelativePath));
            if (!string.IsNullOrWhiteSpace(productionBase)
                && !TestBootstrapContext.TryValidateProductionMembers(
                    state.RepoPath,
                    file.Content,
                    productionBase,
                    TypeMemberConsistencyGuard.BuildProposedTypeDefinitions(
                        WorkflowFindingRules.GetAllProposedFiles(state)),
                    out string productionMemberReason))
            {
                findings.Add(new AgentFinding
                {
                    Severity = FindingSeverity.High,
                    Message = productionMemberReason
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
        RepoContract contract = state.Contract ?? RepoContractDiscoverer.Discover(state.RepoPath);
        findings.AddRange(contract.CollectFrontendFindings(proposedFiles));

        foreach (var profile in contract.LayerConventions.GetActiveProfiles())
        {
            string? canonicalDir = profile.CanonicalDirectory;

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

        foreach (string consumerSuffix in TypeMemberConsistencyGuard.DiscoverConsumerSuffixes(state.RepoPath))
        {
            string filePattern = $"{consumerSuffix}.cs";
            string? canonicalConsumerDir = RagContextComposer.DetectCanonicalDirectoryForFileSuffix(
                                                state.RepoPath,
                                                filePattern,
                                                $"{consumerSuffix}es")
                                            ?? RagContextComposer.DetectCanonicalDirectoryForFileSuffix(
                                                state.RepoPath,
                                                filePattern,
                                                consumerSuffix);

            foreach (var file in proposedFiles)
            {
                string path = file.RelativePath.Replace('\\', '/');
                string fileName = Path.GetFileName(path);
                if (!fileName.EndsWith(filePattern, StringComparison.OrdinalIgnoreCase)
                    || !TypeMemberConsistencyGuard.IsConsumerRelativePath(state.RepoPath, path))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(canonicalConsumerDir)
                    && !path.StartsWith(canonicalConsumerDir + "/", StringComparison.OrdinalIgnoreCase)
                    && !path.Equals(canonicalConsumerDir, StringComparison.OrdinalIgnoreCase))
                {
                    findings.Add(new AgentFinding
                    {
                        Severity = FindingSeverity.High,
                        Message = $"{fileName} should be generated under {canonicalConsumerDir}, not {path}."
                    });
                }
            }
        }

        var duplicatedConsumerNames = Directory
            .EnumerateFiles(state.RepoPath, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                        && !path.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
            .Where(path => TypeMemberConsistencyGuard.IsConsumerRelativePath(
                state.RepoPath,
                Path.GetRelativePath(state.RepoPath, path).Replace('\\', '/')))
            .GroupBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(Path.GetDirectoryName).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .Select(group => group.Key);

        foreach (string duplicated in duplicatedConsumerNames)
        {
            findings.Add(new AgentFinding
            {
                Severity = FindingSeverity.High,
                Message = $"Duplicate consumer file detected in multiple folders: {duplicated}. Keep only canonical consumer path."
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

    private static List<AgentFinding> ValidateArchitectureCoverage(WorkflowState state)
    {
        var findings = new List<AgentFinding>();
        string? architectureSummary = state.Architecture?.Summary;
        if (string.IsNullOrWhiteSpace(architectureSummary))
        {
            return findings;
        }

        ValidateArchitectureDeliverables(
            state,
            WorkflowFindingRules.ExtractBackendPaths(architectureSummary),
            state.Backend?.ProposedFiles,
            "BackendDeveloperAgent",
            findings);

        ValidateArchitectureDeliverables(
            state,
            WorkflowFindingRules.ExtractFrontendPaths(architectureSummary),
            state.Frontend?.ProposedFiles,
            "FrontendDeveloperAgent",
            findings);

        return findings;
    }

    private static void ValidateArchitectureDeliverables(
        WorkflowState state,
        IReadOnlyList<string> requiredPaths,
        IReadOnlyList<GeneratedFile>? proposedFiles,
        string agentName,
        List<AgentFinding> findings)
    {
        if (requiredPaths.Count == 0)
        {
            return;
        }

        proposedFiles ??= new List<GeneratedFile>();
        foreach (string requiredPath in requiredPaths)
        {
            string normalized = requiredPath.Replace('\\', '/');
            GeneratedFile? proposedFile = proposedFiles.FirstOrDefault(file =>
                PathsMatch(file.RelativePath, normalized));

            if (proposedFile is null)
            {
                string? onDiskPath = FindRepoFile(state.RepoPath, normalized);
                if (!string.IsNullOrWhiteSpace(onDiskPath)
                    && !PlaceholderImplementationGuard.ContainsPlaceholderMarkers(File.ReadAllText(onDiskPath)))
                {
                    continue;
                }

                findings.Add(new AgentFinding
                {
                    Severity = FindingSeverity.High,
                    Message =
                        $"Architecture requires '{normalized}' but {agentName} did not include it in proposed files."
                });
                continue;
            }

            if (PlaceholderImplementationGuard.ContainsPlaceholderMarkers(proposedFile.Content))
            {
                findings.Add(new AgentFinding
                {
                    Severity = FindingSeverity.High,
                    Message =
                        $"Architecture deliverable '{normalized}' from {agentName} contains placeholder/stub content."
                });
            }
        }
    }

    private static bool PathsMatch(string proposedRelativePath, string requiredPath)
    {
        string proposed = proposedRelativePath.Replace('\\', '/');
        return proposed.Equals(requiredPath, StringComparison.OrdinalIgnoreCase)
               || proposed.EndsWith("/" + requiredPath, StringComparison.OrdinalIgnoreCase)
               || proposed.EndsWith("/" + Path.GetFileName(requiredPath), StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindRepoFile(string repoPath, string relativePath)
    {
        string direct = Path.Combine(repoPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(direct))
        {
            return direct;
        }

        string fileName = Path.GetFileName(relativePath);
        return Directory
            .EnumerateFiles(repoPath, fileName, SearchOption.AllDirectories)
            .FirstOrDefault(path => !path.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
                                 && !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                                 && !path.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase)
                                 && !path.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase));
    }

    private readonly record struct LayerInterfaceConvention(
        bool LayerUsesInterfaces,
        bool RequireInheritanceClause,
        IReadOnlyList<string> RequiredBaseTokens);
}
