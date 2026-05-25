using System.Text;
using System.Text.RegularExpressions;

namespace workflowX.Infrastructure.CodeApply.DotNet;

/// <summary>
/// C#-specific normalize, catalog, and validation helpers used during apply.
/// </summary>
internal static class CSharpApplySupport
{
    internal static InterfaceCatalog BuildInterfaceCatalog(string repoPath, IReadOnlyList<GeneratedFile> generatedFiles)
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(repoPath, "I*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                || file.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            AddInterfaceMethods(repoPath, File.ReadAllText(file), map);
        }

        foreach (var generated in generatedFiles.Where(f => Path.GetFileName(f.RelativePath).StartsWith("I", StringComparison.OrdinalIgnoreCase)
                                                         && f.RelativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
        {
            AddInterfaceMethods(repoPath, generated.Content, map, overwrite: true, skipProtectedInterfaces: true);
        }

        PropagateInheritedRepositoryMethods(repoPath, generatedFiles, map);
        return new InterfaceCatalog(map);
    }

    internal static TypeNamespaceCatalog BuildTypeNamespaceCatalog(string repoPath, IReadOnlyList<GeneratedFile> generatedFiles)
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(repoPath, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                || file.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            AddTypesWithNamespace(File.ReadAllText(file), map);
        }

        foreach (var generated in generatedFiles.Where(f => f.RelativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
        {
            AddTypesWithNamespace(generated.Content, map);
        }

        return new TypeNamespaceCatalog(map);
    }

    internal static string Normalize(ApplyContext context, string relativePath, string content)
    {
        content = NormalizeRepositoryUsings(relativePath, content, context.Contract.RepositoryInterfacesNamespace);
        content = EnsureReferencedTypeUsings(content, context.TypeNamespaceCatalog);
        content = NormalizeLayerConstructorDependencies(context, relativePath, content);
        content = EnsureConstructorDependencyFields(relativePath, content);
        return NormalizeLayerTestContent(relativePath, content, context.RepoPath);
    }

    internal static bool TryValidate(
        ApplyContext context,
        string relativePath,
        string content,
        bool isOverwritingExistingFile,
        out string reason)
    {
        reason = string.Empty;
        string trimmed = content.Trim();
        bool hasCSharpShape =
            (trimmed.Contains("namespace ", StringComparison.Ordinal)
             || trimmed.Contains("class ", StringComparison.Ordinal)
             || trimmed.Contains("interface ", StringComparison.Ordinal))
            && trimmed.Contains("{", StringComparison.Ordinal)
            && trimmed.Contains("}", StringComparison.Ordinal);
        if (!hasCSharpShape)
        {
            reason = "C# output missing expected type/namespace and block structure.";
            return false;
        }

        if (!CodeExemplarContext.TryValidate(trimmed, out string syntaxReason))
        {
            reason = $"C# syntax check failed: {syntaxReason}";
            return false;
        }

        if (!PlaceholderImplementationGuard.TryValidate(trimmed, out string placeholderReason))
        {
            reason = placeholderReason;
            return false;
        }

        if (!ValidateDynamicLayerConventions(relativePath, trimmed, context.LayerConventions, context.RepoPath, out reason))
        {
            return false;
        }

        if (!InterfaceImplementationGuard.TryValidate(
                context.RepoPath, relativePath, trimmed, context.InterfaceDirectMembers, out reason))
        {
            return false;
        }

        if (!ValidateInterfaceCallParity(context, trimmed, out reason))
        {
            return false;
        }

        if (!ClassMemberAccessGuard.TryValidate(context.RepoPath, relativePath, trimmed, out reason))
        {
            return false;
        }

        if (relativePath.EndsWith("Controller.cs", StringComparison.OrdinalIgnoreCase))
        {
            string? entityContent = ControllerMutationValidationGuard.ResolveEntityContentForCompliance(
                context.RepoPath,
                context.GeneratedFiles,
                relativePath);
            if (!ControllerMutationValidationGuard.TryValidate(
                    context.RepoPath,
                    relativePath,
                    trimmed,
                    entityContent,
                    out reason))
            {
                return false;
            }
        }

        string fileName = Path.GetFileName(relativePath);
        if (fileName.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase))
        {
            if (!TestBootstrapContext.TryValidateTestResolution(trimmed, context.RepoPath, out string bootstrapReason))
            {
                reason = bootstrapReason;
                return false;
            }

            if (!TestBootstrapContext.TryValidateTestLiteralTypes(trimmed, context.RepoPath, out string literalReason))
            {
                reason = literalReason;
                return false;
            }

            string? productionBase = TestCoverageAuditor.ExtractProductionBaseNameFromTestFileName(fileName);
            if (!string.IsNullOrWhiteSpace(productionBase)
                && !TestBootstrapContext.TryValidateProductionMembers(
                    context.RepoPath, trimmed, productionBase, proposedDefinitions: null, out string productionMemberReason))
            {
                reason = productionMemberReason;
                return false;
            }
        }

        return true;
    }

    internal static bool TryValidateDotNet(
        ApplyContext ctx,
        string relativePath,
        string content,
        bool existedBefore,
        string? existingOnDisk,
        ref string validatedContent,
        out string? reason)
    {
        reason = null;

        if (!PreExistingContractGuard.TryValidateOverwrite(
                relativePath, existingOnDisk, content, ctx.WorkflowProposedPaths, ctx.RepoPath, out string contractReason))
        {
            reason = contractReason;
            return false;
        }

        if (TypeMemberConsistencyGuard.IsConsumerRelativePath(ctx.RepoPath, relativePath, ctx.ProposedTypeDefinitions)
            && !TypeMemberConsistencyGuard.TryValidateConsumerContent(
                ctx.RepoPath, relativePath, content, ctx.ProposedTypeDefinitions, out string consumerReason))
        {
            reason = consumerReason;
            return false;
        }

        if (DependencyWiringAuditor.IsCompositionRootPath(relativePath))
        {
            if (!CompositionRootMerger.TryMergeIntoExisting(
                    existingOnDisk ?? string.Empty,
                    content,
                    out string mergedBootstrap,
                    out string? mergeReason,
                    ctx.WorkflowProposedPaths,
                    ctx.RepoPath))
            {
                reason = mergeReason ?? "Rejected invalid composition-root rewrite; append DI registration lines only.";
                return false;
            }

            validatedContent = mergedBootstrap;
            content = mergedBootstrap;
        }

        if (ctx.Contract.Entity is not null
            && !ctx.Contract.Entity.ValidateEntityContent(relativePath, content, out string entityReason))
        {
            reason = entityReason;
            return false;
        }

        if (relativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            && !TryValidate(ctx, relativePath, content, existedBefore, out reason))
        {
            return false;
        }

        return true;
    }

    internal static string? TryExtractDeclaredTypeName(string content)
    {
        Match match = Regex.Match(
            content,
            @"\bpublic\s+(?:partial\s+)?(?:class|interface)\s+([A-Za-z_][A-Za-z0-9_]*)\b");
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// When generated C# declares a type that already exists on disk, resolve to the existing path or reject duplicates.
    /// </summary>
    internal static bool TryResolveDeclaredTypePath(
        string repoPath,
        string relativePath,
        string content,
        ref string resolvedRelativePath,
        out string? issue)
    {
        issue = null;
        string? declaredType = TryExtractDeclaredTypeName(content);
        if (string.IsNullOrWhiteSpace(declaredType))
        {
            return true;
        }

        string targetPath = relativePath;
        string fileName = Path.GetFileName(targetPath);
        var typeMatches = FindFilesDeclaringType(repoPath, declaredType);
        if (typeMatches.Any(match => match.Equals(targetPath, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        string expectedFileName = $"{declaredType}.cs";
        string? preferred = typeMatches.FirstOrDefault(match =>
                                  Path.GetFileName(match).Equals(expectedFileName, StringComparison.OrdinalIgnoreCase))
                              ?? (typeMatches.Count == 1 ? typeMatches[0] : null);
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            resolvedRelativePath = preferred;
            return true;
        }

        if (typeMatches.Count > 1)
        {
            issue =
                $"Type '{declaredType}' is already declared in: {string.Join(", ", typeMatches)}. "
                + $"Update the existing file instead of creating '{fileName}'.";
            return false;
        }

        return true;
    }

    private static bool DeclaresType(string content, string typeName) =>
        Regex.IsMatch(content, $@"\b(?:public\s+)?(?:partial\s+)?class\s+{Regex.Escape(typeName)}\b")
        || Regex.IsMatch(content, $@"\b(?:public\s+)?(?:partial\s+)?interface\s+{Regex.Escape(typeName)}\b");

    private static List<string> FindFilesDeclaringType(string repoPath, string typeName) =>
        Directory
            .EnumerateFiles(repoPath, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                        && !path.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
            .Where(path => DeclaresType(File.ReadAllText(path), typeName))
            .Select(path => Path.GetRelativePath(repoPath, path).Replace('\\', '/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string NormalizeRepositoryUsings(string relativePath, string content, string? repositoryInterfacesNamespace)
    {
        string fileName = Path.GetFileName(relativePath);
        if (!string.IsNullOrWhiteSpace(fileName)
            && fileName.EndsWith("Repository.cs", StringComparison.OrdinalIgnoreCase)
            && !fileName.StartsWith("I", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(repositoryInterfacesNamespace)
            && content.Contains("I", StringComparison.Ordinal)
            && content.Contains("Repository", StringComparison.Ordinal)
            && !content.Contains($"using {repositoryInterfacesNamespace};", StringComparison.Ordinal))
        {
            content = InsertUsingDirective(content, $"using {repositoryInterfacesNamespace};");
        }

        return content;
    }

    private static string EnsureReferencedTypeUsings(string content, TypeNamespaceCatalog catalog)
    {
        if (catalog.IsEmpty)
        {
            return content;
        }

        string? currentNamespace = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith("namespace ", StringComparison.Ordinal))
            ?.Substring("namespace ".Length)
            .Trim();

        var existingUsings = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("using ", StringComparison.Ordinal) && line.EndsWith(";", StringComparison.Ordinal))
            .Select(line => line.Substring("using ".Length, line.Length - "using ".Length - 1).Trim())
            .ToHashSet(StringComparer.Ordinal);

        var declaredTypes = Regex.Matches(content, @"\b(class|interface|record|struct)\s+([A-Za-z_][A-Za-z0-9_]*)")
            .Select(match => match.Groups[2].Value)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var typeName in Regex.Matches(content, @"\b([A-Z][A-Za-z0-9_]*)\b")
                     .Select(match => match.Groups[1].Value)
                     .Distinct(StringComparer.Ordinal)
                     .Where(typeName => !declaredTypes.Contains(typeName)))
        {
            if (!catalog.TryGetUniqueNamespace(typeName, out string? ns) || string.IsNullOrWhiteSpace(ns))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(currentNamespace) && ns.Equals(currentNamespace, StringComparison.Ordinal))
            {
                continue;
            }

            if (existingUsings.Contains(ns))
            {
                continue;
            }

            content = InsertUsingDirective(content, $"using {ns};");
            existingUsings.Add(ns);
        }

        return content;
    }

    private static string InsertUsingDirective(string content, string directive)
    {
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
        if (lines.Any(line => line.Trim().Equals(directive, StringComparison.Ordinal)))
        {
            return content;
        }

        int insertIndex = 0;
        while (insertIndex < lines.Count && lines[insertIndex].Trim().StartsWith("using ", StringComparison.Ordinal))
        {
            insertIndex++;
        }

        lines.Insert(insertIndex, directive);
        var builder = new StringBuilder();
        for (int i = 0; i < lines.Count; i++)
        {
            builder.Append(lines[i]);
            if (i < lines.Count - 1)
            {
                builder.Append('\n');
            }
        }

        return builder.ToString();
    }

    private static string NormalizeLayerTestContent(string relativePath, string content, string repoPath)
    {
        string fileName = Path.GetFileName(relativePath);
        if (!fileName.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase))
        {
            return content;
        }

        content = TestBootstrapContext.NormalizeResolutionAccess(content, repoPath);
        content = TestBootstrapContext.NormalizeTestLiteralTypes(content, repoPath);

        string? productionBaseName = TestCoverageAuditor.ExtractProductionBaseNameFromTestFileName(fileName);
        if (!string.IsNullOrWhiteSpace(productionBaseName))
        {
            content = TestBootstrapContext.SynchronizeProductionMembers(repoPath, productionBaseName, content);
        }

        if (CodeExemplarContext.TryValidate(content, out _)
            && TestBootstrapContext.TryValidateTestResolution(content, repoPath, out _)
            && TestBootstrapContext.TryValidateTestLiteralTypes(content, repoPath, out _)
            && (string.IsNullOrWhiteSpace(productionBaseName)
                || TestBootstrapContext.TryValidateProductionMembers(repoPath, content, productionBaseName, proposedDefinitions: null, out _)))
        {
            return content;
        }

        if (!string.IsNullOrWhiteSpace(productionBaseName)
            && LayerTestTemplateBuilder.TryBuildFromExemplar(repoPath, productionBaseName, out string templated))
        {
            return templated;
        }

        return content;
    }

    private static readonly Regex LayerConstructorPatternRegex = new(
        @"public\s+(?<class>[A-Za-z_][A-Za-z0-9_]*)\s*\((?<params>[^)]*)\)\s*(?::\s*base\s*\((?<baseArgs>[^)]*)\))?\s*\{",
        RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>
    /// Appends constructor DI parameters mirrored from the layer exemplar (e.g. shared ICompanyRepository on controllers).
    /// </summary>
    private static string NormalizeLayerConstructorDependencies(
        ApplyContext context,
        string relativePath,
        string content)
    {
        LayerConventionProfile? profile = context.LayerConventions.ResolveByPath(relativePath);
        if (profile is null || profile.SampleCount < 2)
        {
            return content;
        }

        string fileName = Path.GetFileName(relativePath);
        string className = Path.GetFileNameWithoutExtension(fileName);
        string? subjectBase = LayerConventionProfiles.GetSubjectBaseName(fileName, profile);
        string entity = subjectBase
                        ?? (className.EndsWith(profile.RoleName, StringComparison.OrdinalIgnoreCase)
                            ? className[..^profile.RoleName.Length]
                            : className);

        List<(string Type, string Name)> required = LayerConventionProfiles
            .ResolveRequiredConstructorParameters(context.RepoPath, entity, profile, relativePath)
            .ToList();
        if (required.Count == 0)
        {
            return content;
        }

        Match ctorMatch = FindConstructorMatch(content, className);
        if (!ctorMatch.Success)
        {
            return content;
        }

        HashSet<string> presentTypes = LayerConventionProfiles.ParseConstructorParameterTypes(content, className);
        List<(string Type, string Name)> missing = required
            .Where(parameter => !presentTypes.Contains(parameter.Type))
            .ToList();
        if (missing.Count == 0)
        {
            return content;
        }

        string fieldBlock = string.Join(
                                Environment.NewLine,
                                missing.Select(parameter =>
                                    $"    private readonly {parameter.Type} {ResolveDependencyFieldName(parameter.Type, parameter.Name)};"))
                            + Environment.NewLine;
        content = content.Insert(ctorMatch.Index, fieldBlock);

        ctorMatch = FindConstructorMatch(content, className);
        if (!ctorMatch.Success)
        {
            return content;
        }

        string paramBlock = ctorMatch.Groups["params"].Value.Trim();
        string additions = string.Join(", ", missing.Select(parameter => $"{parameter.Type} {parameter.Name}"));
        string updatedParams = string.IsNullOrWhiteSpace(paramBlock) ? additions : $"{paramBlock}, {additions}";
        string baseClause = ctorMatch.Groups["baseArgs"].Success
            ? $" : base({ctorMatch.Groups["baseArgs"].Value.Trim()})"
            : string.Empty;
        string newCtorHeader = $"public {className}({updatedParams}){baseClause}{{";
        content = string.Concat(
            content.AsSpan(0, ctorMatch.Index),
            newCtorHeader,
            content.AsSpan(ctorMatch.Index + ctorMatch.Length));

        ctorMatch = FindConstructorMatch(content, className);
        if (!ctorMatch.Success)
        {
            return content;
        }

        string assignmentBlock = string.Join(
                                     Environment.NewLine,
                                     missing.Select(parameter =>
                                     {
                                         string fieldName = ResolveDependencyFieldName(parameter.Type, parameter.Name);
                                         return $"        {fieldName} = {parameter.Name};";
                                     }))
                                 + Environment.NewLine;
        int bodyInsertIndex = ctorMatch.Index + ctorMatch.Length;
        return content.Insert(bodyInsertIndex, assignmentBlock);
    }

    private static Match FindConstructorMatch(string content, string className)
    {
        foreach (Match match in LayerConstructorPatternRegex.Matches(content))
        {
            if (match.Groups["class"].Value.Equals(className, StringComparison.Ordinal))
            {
                return match;
            }
        }

        return Match.Empty;
    }

    private static string ResolveDependencyFieldName(string parameterType, string parameterName)
    {
        if (parameterType.StartsWith('I') && parameterType.Length > 1 && char.IsUpper(parameterType[1]))
        {
            return parameterType[1..];
        }

        return char.ToUpperInvariant(parameterName[0]) + parameterName[1..];
    }

    /// <summary>
    /// Ensures every constructor-injected interface dependency has a matching field and assignment.
    /// Fixes recovery output that calls this.EmployeeRepository without declaring the field.
    /// </summary>
    private static string EnsureConstructorDependencyFields(string relativePath, string content)
    {
        string fileName = Path.GetFileName(relativePath);
        if (!fileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return content;
        }

        string className = Path.GetFileNameWithoutExtension(fileName);
        Match ctorMatch = FindConstructorMatch(content, className);
        if (!ctorMatch.Success)
        {
            return content;
        }

        foreach ((string type, string name) in ParseConstructorParameters(ctorMatch.Groups["params"].Value))
        {
            if (!type.StartsWith('I') || type.Length <= 1 || !char.IsUpper(type[1]))
            {
                continue;
            }

            string fieldName = ResolveDependencyFieldName(type, name);
            if (!HasFieldDeclaration(content, fieldName))
            {
                string fieldDecl = $"    private readonly {type} {fieldName};{Environment.NewLine}";
                content = content.Insert(ctorMatch.Index, fieldDecl);
                ctorMatch = FindConstructorMatch(content, className);
                if (!ctorMatch.Success)
                {
                    return content;
                }
            }

            if (!HasFieldAssignment(content, fieldName, name))
            {
                bool usesThisPrefix = content.Contains($"this.{fieldName}", StringComparison.Ordinal);
                string assignment = usesThisPrefix
                    ? $"        this.{fieldName} = {name};{Environment.NewLine}"
                    : $"        {fieldName} = {name};{Environment.NewLine}";
                int bodyInsertIndex = ctorMatch.Index + ctorMatch.Length;
                content = content.Insert(bodyInsertIndex, assignment);
                ctorMatch = FindConstructorMatch(content, className);
                if (!ctorMatch.Success)
                {
                    return content;
                }
            }
        }

        return content;
    }

    private static IEnumerable<(string Type, string Name)> ParseConstructorParameters(string parameterList)
    {
        foreach (string param in parameterList.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            string normalized = Regex.Replace(param.Trim(), @"\s+", " ");
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            string[] parts = normalized.Split(' ');
            if (parts.Length >= 2)
            {
                yield return (parts[0].Trim(), parts[^1].Trim());
            }
        }
    }

    private static bool HasFieldDeclaration(string content, string fieldName) =>
        Regex.IsMatch(
            content,
            $@"(?:private|protected|public|internal)\s+(?:readonly\s+)?[\w<>\[\],\s\?\.]+\s+{Regex.Escape(fieldName)}\s*;",
            RegexOptions.Multiline);

    private static bool HasFieldAssignment(string content, string fieldName, string parameterName) =>
        content.Contains($"{fieldName} = {parameterName}", StringComparison.Ordinal)
        || content.Contains($"this.{fieldName} = {parameterName}", StringComparison.Ordinal);

    private static bool ValidateDynamicLayerConventions(
        string relativePath, string content, LayerConventionProfiles conventions, string repoPath, out string reason)
    {
        reason = string.Empty;
        LayerConventionProfile? profile = conventions.ResolveByPath(relativePath);
        if (profile is null || profile.SampleCount < 2)
        {
            return true;
        }

        string fileName = Path.GetFileName(relativePath);
        string className = Path.GetFileNameWithoutExtension(fileName);
        string role = profile.RoleName;
        string entity = className.EndsWith(role, StringComparison.OrdinalIgnoreCase)
            ? className[..^role.Length]
            : className;

        string? classLine = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith("public class ", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(classLine))
        {
            reason = $"Dynamic {role} contract failed for {fileName}: missing public class declaration.";
            return false;
        }

        if (profile.RequireInheritanceClause && !classLine.Contains(":", StringComparison.Ordinal))
        {
            reason = $"Dynamic {role} contract failed for {fileName}: expected inheritance clause based on existing {role.ToLowerInvariant()} classes.";
            return false;
        }

        if (profile.RequireMatchingRoleInterface)
        {
            string expectedInterface = $"I{entity}{role}";
            if (!classLine.Contains(expectedInterface, StringComparison.Ordinal))
            {
                reason = $"Dynamic {role} contract failed for {fileName}: expected implementation of {expectedInterface}.";
                return false;
            }
        }

        foreach (var token in profile.RequiredInheritedTypeTokens)
        {
            if (!classLine.Contains(token, StringComparison.Ordinal))
            {
                reason = $"Dynamic {role} contract failed for {fileName}: missing inherited token '{token}' used by existing {role.ToLowerInvariant()} classes.";
                return false;
            }
        }

        if (profile.RequireBaseConstructorCall && !content.Contains("base(", StringComparison.Ordinal))
        {
            reason = $"Dynamic {role} contract failed for {fileName}: expected constructor base(...) call used by existing {role.ToLowerInvariant()} classes.";
            return false;
        }

        if (!conventions.ValidateAgainstExemplar(
                repoPath, relativePath, content, Path.GetFileNameWithoutExtension(fileName), profile, out string exemplarReason))
        {
            reason = $"Dynamic {role} contract failed for {fileName}: {exemplarReason}";
            return false;
        }

        string? ctorExemplarPath = LayerConventionProfiles.TryGetConstructorExemplarRelativePath(
            repoPath, entity, profile, relativePath);
        var presentCtorTypes = LayerConventionProfiles.ParseConstructorParameterTypes(content, className);
        string expectedRoleInterface = profile.InterfacePairing.ResolveInterfaceTypeName(entity, profile);
        foreach (var paramType in LayerConventionProfiles.ResolveRequiredConstructorParamTypes(
                     repoPath, entity, profile, relativePath))
        {
            bool present = !string.IsNullOrWhiteSpace(expectedRoleInterface)
                           && paramType.Equals(expectedRoleInterface, StringComparison.Ordinal)
                ? classLine.Contains(paramType, StringComparison.Ordinal)
                : presentCtorTypes.Contains(paramType);
            if (!present)
            {
                string exemplarHint = string.IsNullOrWhiteSpace(ctorExemplarPath)
                    ? string.Empty
                    : $" Mirror constructor dependencies from {ctorExemplarPath}.";
                reason = $"Dynamic {role} contract failed for {fileName}: missing constructor dependency type '{paramType}'.{exemplarHint}";
                return false;
            }
        }

        return true;
    }

    private static bool ValidateInterfaceCallParity(ApplyContext context, string content, out string reason)
    {
        var signatureCatalog = InterfaceCallSignatureGuard.BuildCatalog(context.RepoPath, context.GeneratedFiles);
        return InterfaceCallSignatureGuard.TryValidate(content, signatureCatalog, out reason);
    }

    private static void PropagateInheritedRepositoryMethods(
        string repoPath, IReadOnlyList<GeneratedFile> generatedFiles, Dictionary<string, HashSet<string>> map)
    {
        if (!map.TryGetValue("IRepository", out HashSet<string>? baseMethods) || baseMethods.Count == 0)
        {
            return;
        }

        void Apply(string content)
        {
            foreach (Match ifaceMatch in InterfaceDeclarationRegex.Matches(content))
            {
                string iface = ifaceMatch.Groups[1].Value;
                string declaration = ExtractInterfaceDeclaration(content, ifaceMatch.Index);
                if (!declaration.Contains("IRepository<", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!map.TryGetValue(iface, out HashSet<string>? methods))
                {
                    methods = new HashSet<string>(StringComparer.Ordinal);
                    map[iface] = methods;
                }

                foreach (string method in baseMethods)
                {
                    methods.Add(method);
                }
            }
        }

        foreach (var file in Directory.EnumerateFiles(repoPath, "I*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                || file.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Apply(File.ReadAllText(file));
        }

        foreach (var generated in generatedFiles.Where(f => Path.GetFileName(f.RelativePath).StartsWith('I')
                                                         && f.RelativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
        {
            Apply(generated.Content);
        }
    }

    private static void AddTypesWithNamespace(string content, Dictionary<string, HashSet<string>> map)
    {
        string? ns = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith("namespace ", StringComparison.Ordinal))
            ?.Substring("namespace ".Length)
            .Trim();
        if (string.IsNullOrWhiteSpace(ns))
        {
            return;
        }

        if (ns.EndsWith(';'))
        {
            ns = ns[..^1].Trim();
        }

        foreach (var type in Regex.Matches(content, @"\b(class|interface|record|struct)\s+([A-Za-z_][A-Za-z0-9_]*)")
                     .Select(match => match.Groups[2].Value)
                     .Distinct(StringComparer.Ordinal))
        {
            if (!map.TryGetValue(type, out var set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                map[type] = set;
            }
            set.Add(ns);
        }
    }

    private static readonly Regex InterfaceDeclarationRegex = new(
        @"\binterface\s+(I[A-Za-z0-9_]*)\b",
        RegexOptions.Compiled);

    private static void AddInterfaceMethods(
        string repoPath,
        string content,
        Dictionary<string, HashSet<string>> map,
        bool overwrite = false,
        bool skipProtectedInterfaces = false)
    {
        foreach (Match ifaceMatch in InterfaceDeclarationRegex.Matches(content))
        {
            string iface = ifaceMatch.Groups[1].Value;
            if (skipProtectedInterfaces && PreExistingContractGuard.IsProtectedInterfaceName(iface, repoPath))
            {
                continue;
            }

            if (overwrite || !map.ContainsKey(iface))
            {
                map[iface] = new HashSet<string>(StringComparer.Ordinal);
            }

            string declaration = ExtractInterfaceDeclaration(content, ifaceMatch.Index);
            string body = ExtractInterfaceBody(content, ifaceMatch.Index);
            foreach (Match methodMatch in Regex.Matches(body, @"^\s*(?:[\w<>\[\],\s\?]+\s+)+([A-Za-z_][A-Za-z0-9_]*)\s*\([^;{]*\)\s*;", RegexOptions.Multiline))
            {
                map[iface].Add(methodMatch.Groups[1].Value);
            }

            foreach (Match methodMatch in Regex.Matches(body, @"^\s*([A-Za-z_][A-Za-z0-9_]*)\s*\([^;{]*\)\s*;", RegexOptions.Multiline))
            {
                string methodName = methodMatch.Groups[1].Value;
                if (methodName is not ("get" or "set"))
                {
                    map[iface].Add(methodName);
                }
            }

            if (declaration.Contains("IRepository<", StringComparison.Ordinal)
                && map.TryGetValue("IRepository", out HashSet<string>? repositoryMethods))
            {
                foreach (string method in repositoryMethods)
                {
                    map[iface].Add(method);
                }
            }
        }
    }

    private static string ExtractInterfaceDeclaration(string content, int interfaceIndex)
    {
        int lineStart = content.LastIndexOf('\n', Math.Min(interfaceIndex, content.Length - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        int braceStart = content.IndexOf('{', interfaceIndex);
        return braceStart < 0 ? content[lineStart..] : content[lineStart..braceStart];
    }

    private static string ExtractInterfaceBody(string content, int interfaceIndex)
    {
        int braceStart = content.IndexOf('{', interfaceIndex);
        if (braceStart < 0)
        {
            return string.Empty;
        }

        int depth = 0;
        for (int i = braceStart; i < content.Length; i++)
        {
            if (content[i] == '{')
            {
                depth++;
            }
            else if (content[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return content[braceStart..(i + 1)];
                }
            }
        }

        return content[braceStart..];
    }

    internal static bool IsDotNetSourcePath(string relativePath)
    {
        string extension = Path.GetExtension(relativePath).ToLowerInvariant();
        return extension is ".cs" or ".csproj";
    }

    /// <summary>
    /// Layer-aware apply order: interfaces → entities → indexes → misc → repositories → controllers → tests.
    /// Non-C# paths (e.g. frontend) receive default priority and sort alphabetically among peers.
    /// </summary>
    internal static List<GeneratedFile> OrderForApply(IReadOnlyList<GeneratedFile> files) =>
        files
            .OrderBy(f => GetApplyPriority(f.RelativePath))
            .ThenBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

    internal static int GetApplyPriority(string relativePath)
    {
        string fileName = Path.GetFileName(relativePath);
        if (fileName.StartsWith('I') && fileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (relativePath.Contains("/Entities/", StringComparison.OrdinalIgnoreCase)
            || relativePath.Contains("/Models/", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (fileName.EndsWith("Index.cs", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        if (fileName.EndsWith("Repository.cs", StringComparison.OrdinalIgnoreCase) && !fileName.StartsWith('I'))
        {
            return 4;
        }

        if (fileName.EndsWith("Controller.cs", StringComparison.OrdinalIgnoreCase))
        {
            return 5;
        }

        if (fileName.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase))
        {
            return 6;
        }

        return 3;
    }
}
