using System.Text;
using System.Text.RegularExpressions;

namespace agents_mcp_rag.Infrastructure.CodeApply.DotNet;

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

            AddInterfaceMethods(File.ReadAllText(file), map);
        }

        foreach (var generated in generatedFiles.Where(f => Path.GetFileName(f.RelativePath).StartsWith("I", StringComparison.OrdinalIgnoreCase)
                                                         && f.RelativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
        {
            AddInterfaceMethods(generated.Content, map, overwrite: true, skipProtectedInterfaces: true);
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

        if (!ValidateInterfaceCallParity(relativePath, trimmed, context.InterfaceCatalog, out reason))
        {
            return false;
        }

        if (!ClassMemberAccessGuard.TryValidate(context.RepoPath, relativePath, trimmed, out reason))
        {
            return false;
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
                relativePath, existingOnDisk, content, ctx.WorkflowProposedPaths, out string contractReason))
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
                    ctx.WorkflowProposedPaths))
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
        foreach (var paramType in LayerConventionProfiles.ResolveRequiredConstructorParamTypes(
                     repoPath, entity, profile, relativePath))
        {
            if (!content.Contains(paramType, StringComparison.Ordinal))
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

    private static bool ValidateInterfaceCallParity(
        string relativePath, string content, InterfaceCatalog interfaceCatalog, out string reason)
    {
        reason = string.Empty;
        var fieldTypeMap = Regex.Matches(content, @"(?:private|public|protected)\s+(?:readonly\s+)?(I[A-Za-z0-9_]+)\s+([A-Za-z_][A-Za-z0-9_]*)\s*;")
            .ToDictionary(m => m.Groups[2].Value, m => m.Groups[1].Value, StringComparer.Ordinal);

        foreach (Match call in Regex.Matches(content, @"\b([A-Za-z_][A-Za-z0-9_]*)\.([A-Za-z_][A-Za-z0-9_]*)\s*\("))
        {
            string variable = call.Groups[1].Value;
            string method = call.Groups[2].Value;
            if (!fieldTypeMap.TryGetValue(variable, out string? iface))
            {
                continue;
            }
            if (!interfaceCatalog.TryGetMethods(iface, out var interfaceMethods))
            {
                continue;
            }
            if (!interfaceMethods.Contains(method))
            {
                string knownMembers = string.Join(", ", interfaceMethods.OrderBy(name => name, StringComparer.Ordinal).Take(12));
                reason = $"Method call {variable}.{method}(...) is not defined on {iface}. Known members: {knownMembers}.";
                return false;
            }
        }

        return true;
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
        string content, Dictionary<string, HashSet<string>> map, bool overwrite = false, bool skipProtectedInterfaces = false)
    {
        foreach (Match ifaceMatch in InterfaceDeclarationRegex.Matches(content))
        {
            string iface = ifaceMatch.Groups[1].Value;
            if (skipProtectedInterfaces && PreExistingContractGuard.IsProtectedInterfaceName(iface))
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
