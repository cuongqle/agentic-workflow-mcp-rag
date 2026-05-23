using System.Text.RegularExpressions;
using agents_mcp_rag.Infrastructure;

sealed class LayerContractComplianceRule : IComplianceRule
{
    public string RuleId => "architecture.layer-contracts";
    public string Category => "architecture";

    public bool AppliesTo(ComplianceContext context) =>
        context.Stack.DotNet && context.Contract.LayerConventions.GetActiveProfiles().Any();

    public IEnumerable<AgentFinding> Evaluate(ComplianceContext context)
    {
        var findings = new List<AgentFinding>();
        var proposedPaths = context.ProposedPaths;

        foreach (var profile in context.Contract.LayerConventions.GetActiveProfiles())
        {
            LayerInterfacePairingConvention interfaceConvention =
                context.GetInterfacePairingConvention(profile);
            foreach (string implPath in proposedPaths.Where(path => MatchesProfileImplementation(path, profile)))
            {
                ValidateImplementationPair(
                    context,
                    profile,
                    interfaceConvention,
                    proposedPaths,
                    implPath,
                    findings);
            }
        }

        return findings;
    }

    private static void ValidateImplementationPair(
        ComplianceContext context,
        LayerConventionProfile profile,
        LayerInterfacePairingConvention interfaceConvention,
        IReadOnlySet<string> proposedPaths,
        string implPath,
        List<AgentFinding> findings)
    {
        string fileName = Path.GetFileName(implPath);
        string? subjectBase = LayerConventionProfiles.GetSubjectBaseName(fileName, profile);
        if (string.IsNullOrWhiteSpace(subjectBase))
        {
            return;
        }

        string expectedInterfaceName = interfaceConvention.ResolveInterfaceFileName(subjectBase, profile);
        string expectedInterfaceTypeName = interfaceConvention.ResolveInterfaceTypeName(subjectBase, profile);
        string expectedImplementationName = LayerConventionProfiles.BuildExpectedImplementationFileName(subjectBase, profile);
        string layerLabel = profile.RoleName.ToLowerInvariant();

        bool interfaceInProposed = proposedPaths.Any(path =>
            Path.GetFileName(path).Equals(expectedInterfaceName, StringComparison.OrdinalIgnoreCase));
        string? interfaceFilePathInRepo = LayerInterfacePairingDiscoverer.FindInterfaceFile(
            context.RepoPath,
            interfaceConvention,
            profile,
            subjectBase);
        bool interfaceInRepo = !string.IsNullOrWhiteSpace(interfaceFilePathInRepo);

        if (interfaceConvention.LayerUsesInterfaces
            && !string.IsNullOrWhiteSpace(expectedInterfaceName)
            && !interfaceInProposed
            && !interfaceInRepo)
        {
            findings.Add(new AgentFinding
            {
                Severity = FindingSeverity.High,
                Message =
                    $"Missing {layerLabel} interface for {subjectBase}: expected {expectedInterfaceName} ({expectedInterfaceTypeName}) following discovered {DescribeNamingPattern(interfaceConvention)} conventions."
            });
        }

        string? interfaceContent = context.ProposedFiles
            .FirstOrDefault(f => Path.GetFileName(f.RelativePath).Equals(expectedInterfaceName, StringComparison.OrdinalIgnoreCase))
            ?.Content;
        if (string.IsNullOrWhiteSpace(interfaceContent) && interfaceInRepo && !string.IsNullOrWhiteSpace(interfaceFilePathInRepo))
        {
            interfaceContent = File.ReadAllText(interfaceFilePathInRepo);
        }

        if (string.IsNullOrWhiteSpace(interfaceContent)
            && interfaceConvention.LayerUsesInterfaces
            && !string.IsNullOrWhiteSpace(expectedInterfaceTypeName))
        {
            interfaceContent = FindInterfaceContentByTypeName(
                context,
                expectedInterfaceTypeName,
                proposedPaths);
        }

        if (!string.IsNullOrWhiteSpace(interfaceContent))
        {
            if (interfaceConvention.RequireInheritanceClause && !interfaceContent.Contains(':'))
            {
                findings.Add(new AgentFinding
                {
                    Severity = FindingSeverity.High,
                    Message =
                        $"Interface {expectedInterfaceTypeName} should define an inheritance clause to match existing {layerLabel} interface style."
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
                            $"Interface {expectedInterfaceTypeName} should include base token '{token}' to match {layerLabel} contracts."
                    });
                }
            }
        }

        string? implementationContent = context.ProposedFiles
            .FirstOrDefault(f => Path.GetFileName(f.RelativePath).Equals(expectedImplementationName, StringComparison.OrdinalIgnoreCase))
            ?.Content;
        if (string.IsNullOrWhiteSpace(implementationContent))
        {
            string? implementationPathInRepo = Directory
                .EnumerateFiles(context.RepoPath, expectedImplementationName, SearchOption.AllDirectories)
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

        if (interfaceConvention.LayerUsesInterfaces
            && !string.IsNullOrWhiteSpace(expectedInterfaceTypeName)
            && !ClassLineMatchesToken(classLine, expectedInterfaceTypeName))
        {
            findings.Add(new AgentFinding
            {
                Severity = FindingSeverity.High,
                Message =
                    $"{expectedImplementationName} should implement {expectedInterfaceTypeName} based on discovered {layerLabel} interface pairing."
            });
        }
        else if (profile.RequireMatchingRoleInterface
                 && !string.IsNullOrWhiteSpace(expectedInterfaceTypeName)
                 && !ClassLineMatchesToken(classLine, expectedInterfaceTypeName))
        {
            findings.Add(new AgentFinding
            {
                Severity = FindingSeverity.High,
                Message =
                    $"{expectedImplementationName} should implement {expectedInterfaceTypeName} based on current {layerLabel} conventions."
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
            context.RepoPath,
            subjectBase,
            profile,
            implPath);
        foreach (string paramType in LayerConventionProfiles.ResolveRequiredConstructorParamTypes(
                     context.RepoPath,
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

    private static string? FindInterfaceContentByTypeName(
        ComplianceContext context,
        string interfaceTypeName,
        IReadOnlySet<string> proposedPaths)
    {
        foreach (var file in context.ProposedFiles)
        {
            if (!proposedPaths.Contains(file.RelativePath.Replace('\\', '/')))
            {
                continue;
            }

            if (InterfaceDeclaresType(file.Content, interfaceTypeName))
            {
                return file.Content;
            }
        }

        return null;
    }

    private static bool InterfaceDeclaresType(string content, string interfaceTypeName)
    {
        string normalizedTypeName = NormalizeGenericToken(interfaceTypeName);
        foreach (Match match in InterfaceDeclarationRegex.Matches(content))
        {
            if (NormalizeGenericToken(match.Groups["name"].Value)
                .Equals(normalizedTypeName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string DescribeNamingPattern(LayerInterfacePairingConvention convention) =>
        convention.NamingPattern switch
        {
            InterfaceFileNamingPattern.PrefixedI => "I{Entity}{Role} file naming",
            InterfaceFileNamingPattern.SuffixInterface => "{Entity}{Role}Interface file naming",
            InterfaceFileNamingPattern.SameStemDifferentDirectory => "same-stem interface file in a separate folder",
            _ => "implementation/interface pairing"
        };

    private static bool MatchesProfileImplementation(string path, LayerConventionProfile profile) =>
        LayerConventionProfiles.MatchesImplementationFile(Path.GetFileName(path), profile);

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

    private static readonly Regex InterfaceDeclarationRegex = new(
        @"\b(?:public\s+)?interface\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Compiled);
}

sealed class DependencyWiringComplianceRule : IComplianceRule
{
    public string RuleId => "wiring.missing-di";
    public string Category => "wiring";

    public bool AppliesTo(ComplianceContext context) =>
        context.Stack.DotNet && context.ProposedFiles.Count > 0;

    public IEnumerable<AgentFinding> Evaluate(ComplianceContext context) =>
        DependencyWiringAuditor.ValidateMissingWiring(context.State);
}

sealed class InterfaceImplementationComplianceRule : FileComplianceRule
{
    public override string RuleId => "contract.interface-implementation";
    public override string Category => "contract";

    public override bool AppliesTo(ComplianceContext context) =>
        context.Stack.DotNet && context.ProposedFiles.Count > 0;

    protected override bool ShouldInspect(GeneratedFile file, ComplianceContext context)
    {
        if (!file.RelativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string fileName = Path.GetFileName(file.RelativePath);
        return !fileName.StartsWith('I') && file.Content.Contains("public class ", StringComparison.Ordinal);
    }

    protected override AgentFinding? ValidateFile(GeneratedFile file, ComplianceContext context)
    {
        if (InterfaceImplementationGuard.TryValidate(
                context.RepoPath,
                file.RelativePath.Replace('\\', '/'),
                file.Content,
                context.GetInterfaceMemberCatalog(),
                out string reason))
        {
            return null;
        }

        return new AgentFinding
        {
            Severity = FindingSeverity.High,
            Message = reason
        };
    }
}

sealed class TypeMemberConsistencyComplianceRule : FileComplianceRule
{
    public override string RuleId => "contract.type-member-consistency";
    public override string Category => "contract";

    public override bool AppliesTo(ComplianceContext context) =>
        context.Stack.DotNet && context.ProposedFiles.Count > 0;

    protected override bool ShouldInspect(GeneratedFile file, ComplianceContext context)
    {
        var proposedDefinitions = context.GetProposedTypeDefinitions();
        return TypeMemberConsistencyGuard.IsConsumerRelativePath(
            context.RepoPath,
            file.RelativePath.Replace('\\', '/'),
            proposedDefinitions);
    }

    protected override AgentFinding? ValidateFile(GeneratedFile file, ComplianceContext context)
    {
        var proposedDefinitions = context.GetProposedTypeDefinitions();
        if (TypeMemberConsistencyGuard.TryValidateConsumerContent(
                context.RepoPath,
                file.RelativePath.Replace('\\', '/'),
                file.Content,
                proposedDefinitions,
                out string reason))
        {
            return null;
        }

        return new AgentFinding
        {
            Severity = FindingSeverity.High,
            Message = reason
        };
    }
}

sealed class TestBootstrapResolutionComplianceRule : FileComplianceRule
{
    public override string RuleId => "testing.bootstrap-resolution";
    public override string Category => "testing";

    public override bool AppliesTo(ComplianceContext context) =>
        context.Stack.DotNet && context.ProposedFiles.Count > 0;

    protected override bool ShouldInspect(GeneratedFile file, ComplianceContext context) =>
        file.RelativePath.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase);

    protected override AgentFinding? ValidateFile(GeneratedFile file, ComplianceContext context)
    {
        if (!TestBootstrapContext.TryValidateTestResolution(file.Content, context.RepoPath, out string reason))
        {
            return new AgentFinding
            {
                Severity = FindingSeverity.High,
                Message = reason
            };
        }

        if (!TestBootstrapContext.TryValidateTestLiteralTypes(file.Content, context.RepoPath, out string literalReason))
        {
            return new AgentFinding
            {
                Severity = FindingSeverity.High,
                Message = literalReason
            };
        }

        string? productionBase = TestCoverageAuditor.ExtractProductionBaseNameFromTestFileName(
            Path.GetFileName(file.RelativePath));
        if (!string.IsNullOrWhiteSpace(productionBase)
            && !TestBootstrapContext.TryValidateProductionMembers(
                context.RepoPath,
                file.Content,
                productionBase,
                context.GetProposedTypeDefinitions(),
                out string productionMemberReason))
        {
            return new AgentFinding
            {
                Severity = FindingSeverity.High,
                Message = productionMemberReason
            };
        }

        return null;
    }
}

sealed class TestCoverageComplianceRule : IComplianceRule
{
    public string RuleId => "testing.missing-tests";
    public string Category => "testing";

    public bool AppliesTo(ComplianceContext context) => context.Stack.DotNet;

    public IEnumerable<AgentFinding> Evaluate(ComplianceContext context) =>
        TestCoverageAuditor.ValidateMissingTests(context.State);
}

sealed class TestPackageConventionComplianceRule : FileComplianceRule
{
    public override string RuleId => "testing.package-conventions";
    public override string Category => "testing";

    public override bool AppliesTo(ComplianceContext context) =>
        context.Stack.DotNet && context.ProposedFiles.Count > 0;

    protected override bool ShouldInspect(GeneratedFile file, ComplianceContext context) => true;

    protected override AgentFinding? ValidateFile(GeneratedFile file, ComplianceContext context)
    {
        if (ProjectPackageAuditor.TryValidateTestPackages(
                context.RepoPath,
                file.RelativePath.Replace('\\', '/'),
                file.Content,
                out string reason))
        {
            return null;
        }

        return new AgentFinding
        {
            Severity = FindingSeverity.Medium,
            Message = reason
        };
    }
}

sealed class DotNetPathConventionComplianceRule : IComplianceRule
{
    public string RuleId => "architecture.dotnet-path-conventions";
    public string Category => "architecture";

    public bool AppliesTo(ComplianceContext context) =>
        context.Stack.DotNet && context.ProposedFiles.Count > 0;

    public IEnumerable<AgentFinding> Evaluate(ComplianceContext context)
    {
        var findings = new List<AgentFinding>();

        foreach (var profile in context.Contract.LayerConventions.GetActiveProfiles())
        {
            string? canonicalDir = profile.CanonicalDirectory;

            foreach (var file in context.ProposedFiles)
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

        foreach (string consumerSuffix in TypeMemberConsistencyGuard.DiscoverConsumerSuffixes(context.RepoPath))
        {
            string filePattern = $"{consumerSuffix}.cs";
            string? canonicalConsumerDir = CSharpRagContextSupport.DetectCanonicalDirectoryForFileSuffix(
                                                context.RepoPath,
                                                filePattern,
                                                $"{consumerSuffix}es")
                                            ?? CSharpRagContextSupport.DetectCanonicalDirectoryForFileSuffix(
                                                context.RepoPath,
                                                filePattern,
                                                consumerSuffix);

            foreach (var file in context.ProposedFiles)
            {
                string path = file.RelativePath.Replace('\\', '/');
                string fileName = Path.GetFileName(path);
                if (!fileName.EndsWith(filePattern, StringComparison.OrdinalIgnoreCase)
                    || !TypeMemberConsistencyGuard.IsConsumerRelativePath(context.RepoPath, path))
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
            .EnumerateFiles(context.RepoPath, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                        && !path.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
            .Where(path => TypeMemberConsistencyGuard.IsConsumerRelativePath(
                context.RepoPath,
                Path.GetRelativePath(context.RepoPath, path).Replace('\\', '/')))
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
}

static class DotNetComplianceRules
{
    public static IReadOnlyList<IComplianceRule> All { get; } =
    [
        new DotNetPathConventionComplianceRule(),
        new LayerContractComplianceRule(),
        new DependencyWiringComplianceRule(),
        new InterfaceImplementationComplianceRule(),
        new TypeMemberConsistencyComplianceRule(),
        new TestBootstrapResolutionComplianceRule(),
        new TestCoverageComplianceRule(),
        new TestPackageConventionComplianceRule()
    ];
}
