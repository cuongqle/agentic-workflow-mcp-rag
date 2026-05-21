using System.Text.RegularExpressions;
using agents_mcp_rag.Infrastructure;

static class CompilationContractContextBuilder
{
    public static string Build(
        string repoPath,
        IReadOnlyList<string> allowedFiles,
        IReadOnlyList<AgentFinding>? buildFindings = null)
    {
        var contextLines = new List<string>
        {
            "Use only these declared contracts and signatures."
        };

        AppendLayerTestExemplarContext(repoPath, allowedFiles, buildFindings, contextLines);
        AppendEntityIndexPairContext(repoPath, allowedFiles, buildFindings, contextLines);

        string exemplarContext = CodeExemplarContext.BuildForCompilationFix(repoPath, allowedFiles);
        if (!string.IsNullOrWhiteSpace(exemplarContext))
        {
            contextLines.Add(exemplarContext);
        }

        string? wiringContext = DependencyWiringAuditor.BuildRegistrationContext(repoPath);
        if (!string.IsNullOrWhiteSpace(wiringContext))
        {
            contextLines.Add(wiringContext);
        }

        AppendAuthoritativeInfrastructureContracts(repoPath, contextLines);
        AppendRepositoryQueryContract(repoPath, allowedFiles, contextLines);
        AppendControllerContract(repoPath, allowedFiles, contextLines);
        AppendInheritedTypeMembers(repoPath, allowedFiles, contextLines);
        string? interfaceImplContext = InterfaceImplementationGuard.BuildCompilationFixContext(
            repoPath,
            buildFindings,
            allowedFiles);
        if (!string.IsNullOrWhiteSpace(interfaceImplContext))
        {
            contextLines.Add(interfaceImplContext);
        }

        string? packageContext = ProjectPackageAuditor.BuildTestPackageContext(repoPath);
        if (!string.IsNullOrWhiteSpace(packageContext))
        {
            contextLines.Add(packageContext);
        }

        foreach (var finding in buildFindings ?? Array.Empty<AgentFinding>())
        {
            foreach (Match match in Regex.Matches(
                         finding.Message,
                         @"type or namespace name\s+'([A-Za-z_][A-Za-z0-9_]*)'\s+could not be found",
                         RegexOptions.IgnoreCase))
            {
                contextLines.Add(
                    $"CS0246: missing namespace/type '{match.Groups[1].Value}' — workflow will dotnet add matching NuGet package to test project if mapped; prefer removing unused usings and mirroring exemplar test style.");
            }
        }

        AppendTestBootstrapContext(repoPath, allowedFiles, contextLines);

        foreach (var relative in allowedFiles.Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)).Take(24))
        {
            string absolute = Path.Combine(repoPath, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(absolute))
            {
                continue;
            }

            string content = File.ReadAllText(absolute);
            contextLines.Add($"File: {relative}");
            foreach (Match match in Regex.Matches(content, @"public\s+(?:interface|class)\s+[^{\r\n]+"))
            {
                contextLines.Add($"- {match.Value.Trim()}");
            }
            foreach (Match match in Regex.Matches(content, @"public\s+[A-Za-z0-9_<>\[\],\s\?]+\s+[A-Za-z_][A-Za-z0-9_]*\s*\([^)]*\)"))
            {
                string signature = Regex.Replace(match.Value.Trim(), @"\s+", " ");
                contextLines.Add($"- {signature}");
            }
            foreach (Match match in Regex.Matches(content, @"public\s+[A-Za-z0-9_<>\[\],\s\?]+\s+[A-Za-z_][A-Za-z0-9_]*\s*\{\s*get;\s*(set;)?\s*\}"))
            {
                string property = Regex.Replace(match.Value.Trim(), @"\s+", " ");
                contextLines.Add($"- {property}");
            }
        }

        if (contextLines.Count == 1)
        {
            return "- No explicit contract declarations were collected.";
        }

        string joined = string.Join('\n', contextLines);
        return joined.Length > 6000 ? joined[..6000] + "\n[contract context truncated]" : joined;
    }

    private static void AppendLayerTestExemplarContext(
        string repoPath,
        IReadOnlyList<string> allowedFiles,
        IReadOnlyList<AgentFinding>? buildFindings,
        List<string> contextLines)
    {
        bool hasTestTargets = allowedFiles.Any(BuildFailureClassifier.IsTestArtifactPath)
            || (buildFindings is not null && buildFindings.Any(f => BuildFailureClassifier.ClassifyMessage(f.Message) == BuildFailureScope.Test));
        if (!hasTestTargets)
        {
            return;
        }

        string? targetTestFile = allowedFiles
            .FirstOrDefault(path => Path.GetFileName(path).EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase));
        string? productionBaseName = targetTestFile is not null
            ? TestCoverageAuditor.ExtractProductionBaseNameFromTestFileName(Path.GetFileName(targetTestFile))
            : null;

        TestConvention? convention = !string.IsNullOrWhiteSpace(productionBaseName)
            ? TestCoverageAuditor.FindConventionForProductionBase(repoPath, productionBaseName)
            : TestCoverageAuditor.DiscoverTestConventions(repoPath).FirstOrDefault();

        if (convention is null)
        {
            return;
        }

        string testsAbsoluteDir = Path.Combine(
            repoPath,
            convention.TestDirectory.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(testsAbsoluteDir))
        {
            return;
        }

        string exemplarGlob = $"*{Path.GetFileNameWithoutExtension(convention.ProductionFileSuffix)}Tests.cs";
        string? exemplarPath = Directory
            .EnumerateFiles(testsAbsoluteDir, exemplarGlob, SearchOption.TopDirectoryOnly)
            .FirstOrDefault();
        exemplarPath ??= Directory
            .EnumerateFiles(testsAbsoluteDir, "*Tests.cs", SearchOption.TopDirectoryOnly)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(exemplarPath))
        {
            return;
        }

        string exemplarRelative = Path.GetRelativePath(repoPath, exemplarPath).Replace('\\', '/');
        string exemplarContent = File.ReadAllText(exemplarPath);
        if (exemplarContent.Length > 2500)
        {
            exemplarContent = exemplarContent[..2500] + "\n// [exemplar truncated]";
        }

        string layer = Path.GetFileNameWithoutExtension(convention.ProductionFileSuffix);
        contextLines.Add($"{layer} test exemplar (mirror structure exactly; valid C# only):");
        contextLines.Add($"File: {exemplarRelative}");
        contextLines.Add(exemplarContent);
    }

    private static void AppendEntityIndexPairContext(
        string repoPath,
        IReadOnlyList<string> allowedFiles,
        IReadOnlyList<AgentFinding>? buildFindings,
        List<string> contextLines)
    {
        var entityNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var relative in allowedFiles)
        {
            if (TypeMemberConsistencyGuard.TryResolveDefinitionType(
                    repoPath,
                    relative.Replace('\\', '/'),
                    proposedDefinitions: null,
                    out string definitionTypeName))
            {
                entityNames.Add(definitionTypeName);
            }
        }

        foreach (var finding in buildFindings ?? Array.Empty<AgentFinding>())
        {
            foreach (Match match in Regex.Matches(
                         finding.Message,
                         @"'([A-Za-z_][A-Za-z0-9_]*)'\s+does not contain a definition for\s+'([A-Za-z_][A-Za-z0-9_]*)'",
                         RegexOptions.IgnoreCase))
            {
                string typeName = match.Groups[1].Value;
                string missingMember = match.Groups[2].Value;
                entityNames.Add(typeName);

                string? targetFile = allowedFiles.FirstOrDefault(path =>
                    Path.GetFileNameWithoutExtension(path).Equals(typeName, StringComparison.Ordinal));
                if (!string.IsNullOrWhiteSpace(targetFile))
                {
                    string absolute = Path.Combine(repoPath, targetFile.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(absolute))
                    {
                        string? memberContext = ClassMemberAccessGuard.BuildBaseTypeContext(
                            repoPath,
                            File.ReadAllText(absolute));
                        if (!string.IsNullOrWhiteSpace(memberContext))
                        {
                            contextLines.Add(
                                $"CS1061 on {typeName}: '{missingMember}' is not an instance member. {memberContext}");
                            continue;
                        }
                    }
                }

                string? productionContract = TestBootstrapContext.BuildProductionContractContext(repoPath, typeName);
                if (!string.IsNullOrWhiteSpace(productionContract))
                {
                    contextLines.Add(
                        $"CS1061 on test target {typeName}: do not call '{missingMember}' — it is not on the production type. {productionContract}");
                }
                else
                {
                    contextLines.Add(
                        $"CS1061 contract: type '{typeName}' must declare member '{missingMember}' (add member or use an existing declared/base member).");
                }
            }
        }

        foreach (string entityName in entityNames.Where(n => n.Length > 1))
        {
            string? entityPath = Directory
                .EnumerateFiles(repoPath, $"{entityName}.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                               && !path.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path.Contains("/Entities/", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(path => path.Length)
                .Select(path => Path.GetRelativePath(repoPath, path).Replace('\\', '/'))
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(entityPath))
            {
                continue;
            }

            string absolute = Path.Combine(repoPath, entityPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(absolute))
            {
                continue;
            }

            string content = File.ReadAllText(absolute);
            if (content.Length > 2200)
            {
                content = content[..2200] + "\n// [entity truncated]";
            }

            var declaredMembers = TypeMemberConsistencyGuard.ExtractDeclaredMembersForContext(content);
            contextLines.Add($"Authoritative definition type for projection consumers ({entityName}):");
            contextLines.Add($"File: {entityPath}");
            if (declaredMembers.Count > 0)
            {
                contextLines.Add(
                    $"Consumer projections may ONLY reference these members: {string.Join(", ", declaredMembers.OrderBy(p => p, StringComparer.Ordinal))}");
            }

            contextLines.Add(content);
        }
    }

    private static void AppendTestBootstrapContext(
        string repoPath,
        IReadOnlyList<string> allowedFiles,
        List<string> contextLines)
    {
        bool hasTestTarget = allowedFiles.Any(path =>
            Path.GetFileName(path).EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase)
            || BuildFailureClassifier.IsTestArtifactPath(path));
        if (!hasTestTarget)
        {
            return;
        }

        string? bootstrapContext = TestBootstrapContext.BuildContext(repoPath);
        if (!string.IsNullOrWhiteSpace(bootstrapContext))
        {
            contextLines.Add(bootstrapContext);
        }

        string? targetTest = allowedFiles
            .FirstOrDefault(path => Path.GetFileName(path).EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(targetTest))
        {
            return;
        }

        string? productionBase = TestCoverageAuditor.ExtractProductionBaseNameFromTestFileName(
            Path.GetFileName(targetTest));
        if (!string.IsNullOrWhiteSpace(productionBase)
            && LayerTestTemplateBuilder.TryBuildFromExemplar(repoPath, productionBase, out string layerTemplate))
        {
            contextLines.Add("Test file template (mirror structure and bootstrap calls exactly):");
            contextLines.Add(layerTemplate.Length > 2000 ? layerTemplate[..2000] + "\n// [truncated]" : layerTemplate);
        }
    }

    private static void AppendRepositoryQueryContract(
        string repoPath,
        IReadOnlyList<string> allowedFiles,
        List<string> contextLines)
    {
        bool needsRepositoryContext = allowedFiles.Any(path =>
            Path.GetFileName(path).EndsWith("Repository.cs", StringComparison.OrdinalIgnoreCase)
            && !Path.GetFileName(path).Equals("Repository.cs", StringComparison.OrdinalIgnoreCase));
        if (!needsRepositoryContext)
        {
            return;
        }

        string? baseRepositoryPath = Directory
            .EnumerateFiles(repoPath, "Repository.cs", SearchOption.AllDirectories)
            .FirstOrDefault(path => Path.GetFileName(path).Equals("Repository.cs", StringComparison.OrdinalIgnoreCase)
                                 && path.Contains("Repository", StringComparison.OrdinalIgnoreCase)
                                 && !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(baseRepositoryPath))
        {
            string content = File.ReadAllText(baseRepositoryPath);
            if (content.Length > 1600)
            {
                content = content[..1600] + "\n// [truncated]";
            }

            contextLines.Add("Repository base query contract (use index name string — never call Query<T>() without indexName):");
            contextLines.Add($"File: {Path.GetRelativePath(repoPath, baseRepositoryPath).Replace('\\', '/')}");
            contextLines.Add(content);
        }

        string? exemplarRepo = Directory
            .GetFiles(repoPath, "*Repository.cs", SearchOption.AllDirectories)
            .Where(path => !Path.GetFileName(path).StartsWith('I')
                        && !Path.GetFileName(path).Equals("Repository.cs", StringComparison.OrdinalIgnoreCase)
                        && !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(path => File.ReadAllText(path).Contains("typeof(", StringComparison.Ordinal)
                                   && File.ReadAllText(path).Contains(".Name", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(exemplarRepo))
        {
            return;
        }

        string exemplarContent = File.ReadAllText(exemplarRepo);
        if (exemplarContent.Length > 1400)
        {
            exemplarContent = exemplarContent[..1400] + "\n// [truncated]";
        }

        contextLines.Add("Exemplar repository index usage (pass typeof({Entity}Index).Name as indexName):");
        contextLines.Add($"File: {Path.GetRelativePath(repoPath, exemplarRepo).Replace('\\', '/')}");
        contextLines.Add(exemplarContent);
    }

    private static void AppendControllerContract(
        string repoPath,
        IReadOnlyList<string> allowedFiles,
        List<string> contextLines)
    {
        var controllerBaseNames = allowedFiles
            .Select(path => Path.GetFileName(path))
            .Select(name =>
            {
                if (name.EndsWith("Controller.cs", StringComparison.OrdinalIgnoreCase)
                    && !name.Equals("Controller.cs", StringComparison.OrdinalIgnoreCase))
                {
                    return Path.GetFileNameWithoutExtension(name);
                }

                if (name.EndsWith("ControllerTests.cs", StringComparison.OrdinalIgnoreCase))
                {
                    return TestCoverageAuditor.ExtractProductionBaseNameFromTestFileName(name);
                }

                return null;
            })
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (controllerBaseNames.Count == 0)
        {
            return;
        }

        foreach (string? controllerBase in controllerBaseNames)
        {
            if (string.IsNullOrWhiteSpace(controllerBase))
            {
                continue;
            }

            string? contract = TestBootstrapContext.BuildProductionContractContext(repoPath, controllerBase);
            if (!string.IsNullOrWhiteSpace(contract))
            {
                contextLines.Add(contract);
            }
        }

        bool hasControllerTarget = controllerBaseNames.Any(name =>
            name.EndsWith("Controller", StringComparison.Ordinal));
        if (!hasControllerTarget)
        {
            return;
        }

        string? repositoryInterfacePath = Directory
            .EnumerateFiles(repoPath, "IRepository.cs", SearchOption.AllDirectories)
            .FirstOrDefault(path => !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(repositoryInterfacePath))
        {
            string content = File.ReadAllText(repositoryInterfacePath);
            if (content.Length > 1200)
            {
                content = content[..1200] + "\n// [truncated]";
            }

            contextLines.Add("Repository interface base contract (controllers must call these members — e.g. Insert(entity), not invented names):");
            contextLines.Add($"File: {Path.GetRelativePath(repoPath, repositoryInterfacePath).Replace('\\', '/')}");
            contextLines.Add(content);
        }

        string? exemplarController = Directory
            .GetFiles(repoPath, "*Controller.cs", SearchOption.AllDirectories)
            .Where(path => !Path.GetFileName(path).Equals("Controller.cs", StringComparison.OrdinalIgnoreCase)
                        && !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(path => File.ReadAllText(path).Contains(".Insert(", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(exemplarController))
        {
            return;
        }

        string exemplarContent = File.ReadAllText(exemplarController);
        if (exemplarContent.Length > 1400)
        {
            exemplarContent = exemplarContent[..1400] + "\n// [truncated]";
        }

        contextLines.Add("Exemplar controller persistence call (mirror Insert/GetById/Count patterns):");
        contextLines.Add($"File: {Path.GetRelativePath(repoPath, exemplarController).Replace('\\', '/')}");
        contextLines.Add(exemplarContent);
    }

    private static void AppendInheritedTypeMembers(
        string repoPath,
        IReadOnlyList<string> allowedFiles,
        List<string> contextLines)
    {
        foreach (string relative in allowedFiles.Where(p => p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)).Take(12))
        {
            string absolute = Path.Combine(repoPath, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(absolute))
            {
                continue;
            }

            string content = File.ReadAllText(absolute);
            string? memberContext = ClassMemberAccessGuard.BuildBaseTypeContext(repoPath, content);
            if (string.IsNullOrWhiteSpace(memberContext))
            {
                continue;
            }

            contextLines.Add(memberContext);
        }
    }

    private static void AppendAuthoritativeInfrastructureContracts(string repoPath, List<string> contextLines)
    {
        string? dbStorePath = Directory
            .EnumerateFiles(repoPath, "IDbStore.cs", SearchOption.AllDirectories)
            .FirstOrDefault(path => path.Contains("DbStore", StringComparison.OrdinalIgnoreCase)
                                 && !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(dbStorePath))
        {
            return;
        }

        string content = File.ReadAllText(dbStorePath);
        if (content.Length > 1800)
        {
            content = content[..1800] + "\n// [truncated]";
        }

        contextLines.Add("Authoritative store interface contract (read-only — do not add or change members):");
        contextLines.Add($"File: {Path.GetRelativePath(repoPath, dbStorePath).Replace('\\', '/')}");
        contextLines.Add(content);
    }
}
