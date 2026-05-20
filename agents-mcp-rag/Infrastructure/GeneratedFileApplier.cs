using System.Text.RegularExpressions;
using System.Text;
using agents_mcp_rag.Infrastructure;

static class GeneratedFileApplier
{
    public static Task<ApplyResult> ApplyAsync(WorkflowState state)
    {
        var applied = new List<string>();
        var rejected = new List<ApplyIssue>();
        var appliedChanges = new List<AppliedFileChange>();
        string repoRoot = Path.GetFullPath(state.RepoPath);
        var canonical = DetectCanonicalRoots(state.RepoPath);
        var conventions = LayerConventionProfileBuilder.Build(state.RepoPath);
        var generatedFiles = EnumerateGeneratedFiles(state).ToList();
        var interfaceCatalog = BuildInterfaceCatalog(state.RepoPath, generatedFiles);
        var typeNamespaceCatalog = BuildTypeNamespaceCatalog(state.RepoPath, generatedFiles);

        foreach (var generatedFile in generatedFiles)
        {
            string relativePath = generatedFile.RelativePath.Replace('\\', '/').Trim();
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                continue;
            }

            if (Path.IsPathRooted(relativePath))
            {
                continue;
            }

            relativePath = NormalizeToCanonical(relativePath, generatedFile.Content, canonical);
            if (!TryResolveDuplicateToExistingPath(state.RepoPath, relativePath, generatedFile.Content, out relativePath, out string? duplicateResolutionIssue))
            {
                rejected.Add(new ApplyIssue(relativePath, duplicateResolutionIssue ?? "Duplicate resolution failed."));
                continue;
            }
            relativePath = relativePath.TrimStart('/');
            string fullPath = Path.GetFullPath(Path.Combine(state.RepoPath, relativePath));
            if (!fullPath.StartsWith(repoRoot, StringComparison.OrdinalIgnoreCase))
            {
                rejected.Add(new ApplyIssue(relativePath, "Rejected path outside repository root."));
                continue;
            }

            string? directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string content = NormalizeContent(relativePath, generatedFile.Content, canonical, typeNamespaceCatalog);
            content = TryNormalizeRepositoryTestContent(relativePath, content, state.RepoPath);
            if (!IsLikelyValidSource(relativePath, content, File.Exists(fullPath), state.RepoPath, conventions, interfaceCatalog, out string reason))
            {
                rejected.Add(new ApplyIssue(relativePath, reason));
                continue;
            }

            bool existedBefore = File.Exists(fullPath);
            string? previousContent = existedBefore ? File.ReadAllText(fullPath) : null;
            File.WriteAllText(fullPath, content);
            applied.Add(relativePath);
            appliedChanges.Add(new AppliedFileChange(relativePath, existedBefore, previousContent));
        }

        return Task.FromResult(new ApplyResult(applied, rejected, appliedChanges));
    }

    public static Task RollbackAsync(string repoPath, IReadOnlyList<AppliedFileChange> changes)
    {
        string repoRoot = Path.GetFullPath(repoPath);
        foreach (var change in changes.Reverse())
        {
            string fullPath = Path.GetFullPath(Path.Combine(repoPath, change.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!fullPath.StartsWith(repoRoot, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (change.ExistedBeforeApply)
            {
                File.WriteAllText(fullPath, change.PreviousContent ?? string.Empty);
            }
            else if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }

        return Task.CompletedTask;
    }

    private static string NormalizeToCanonical(string relativePath, string content, CanonicalRoots roots)
    {
        string path = relativePath.Replace('\\', '/').TrimStart('/');
        string fileName = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return path;
        }

        if (fileName.EndsWith("Controller.cs", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(roots.WebApiControllers))
        {
            return $"{roots.WebApiControllers}/{fileName}";
        }

        if (fileName.StartsWith("I", StringComparison.OrdinalIgnoreCase)
            && fileName.EndsWith("Repository.cs", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(roots.RepositoryInterfaces))
        {
            return $"{roots.RepositoryInterfaces}/{fileName}";
        }

        if (!fileName.StartsWith("I", StringComparison.OrdinalIgnoreCase)
            && fileName.EndsWith("Repository.cs", StringComparison.OrdinalIgnoreCase)
            && !fileName.Equals("Repository.cs", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(roots.RepositoryRoot))
        {
            return $"{roots.RepositoryRoot}/{fileName}";
        }

        if (fileName.EndsWith("Index.cs", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(roots.RepositoryIndexes))
        {
            return $"{roots.RepositoryIndexes}/{fileName}";
        }

        if (path.Contains("/Entities/", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(roots.RepositoryEntities))
        {
            return $"{roots.RepositoryEntities}/{fileName}";
        }

        if (IsEntityLikeFile(path, fileName, content)
            && !string.IsNullOrWhiteSpace(roots.RepositoryEntities))
        {
            return $"{roots.RepositoryEntities}/{fileName}";
        }

        if (path.Contains("/RepositoryTest/", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(roots.UnitTestRepositoryTests))
        {
            return $"{roots.UnitTestRepositoryTests}/{fileName}";
        }

        return path;
    }

    private static bool IsEntityLikeFile(string path, string fileName, string content)
    {
        if (!fileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (fileName.StartsWith("I", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (fileName.EndsWith("Repository.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith("Controller.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith("Service.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith("Index.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith("Expression.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string className = Path.GetFileNameWithoutExtension(fileName);
        if (!content.Contains($"class {className}", StringComparison.Ordinal))
        {
            return false;
        }

        if (path.Contains("/Entities/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        bool hasSimpleModelSignals = content.Contains("{ get; set; }", StringComparison.Ordinal)
                                     || content.Contains("[DataMember", StringComparison.Ordinal)
                                     || content.Contains("[JsonProperty", StringComparison.Ordinal);
        return hasSimpleModelSignals;
    }

    private static bool TryResolveDuplicateToExistingPath(
        string repoPath,
        string relativePath,
        string content,
        out string resolvedRelativePath,
        out string? issue)
    {
        resolvedRelativePath = relativePath.Replace('\\', '/').TrimStart('/');
        issue = null;

        string fileName = Path.GetFileName(resolvedRelativePath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return true;
        }

        bool isClassLike = fileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                           || fileName.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
                           || fileName.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)
                           || fileName.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase);
        if (!isClassLike)
        {
            return true;
        }

        var existingMatches = Directory.EnumerateFiles(repoPath, fileName, SearchOption.AllDirectories)
            .Where(path => !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                        && !path.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
                        && !path.Contains("/node_modules/", StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetRelativePath(repoPath, path).Replace('\\', '/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (existingMatches.Count == 0)
        {
            return true;
        }

        string targetPath = resolvedRelativePath;
        if (existingMatches.Any(match => match.Equals(targetPath, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (existingMatches.Count == 1)
        {
            resolvedRelativePath = existingMatches[0];
            return true;
        }

        string className = Path.GetFileNameWithoutExtension(fileName);
        var classTokenMatches = existingMatches
            .Where(path =>
            {
                string absolute = Path.Combine(repoPath, path.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(absolute))
                {
                    return false;
                }
                string existing = File.ReadAllText(absolute);
                return existing.Contains($"class {className}", StringComparison.Ordinal)
                       || existing.Contains($"interface {className}", StringComparison.Ordinal)
                       || existing.Contains($"function {className}", StringComparison.Ordinal);
            })
            .ToList();

        if (classTokenMatches.Count == 1)
        {
            resolvedRelativePath = classTokenMatches[0];
            return true;
        }

        issue = $"Ambiguous duplicate target for '{fileName}'. Existing candidates: {string.Join(", ", existingMatches)}";
        return false;
    }

    private static string NormalizeContent(
        string relativePath,
        string content,
        CanonicalRoots roots,
        TypeNamespaceCatalog typeNamespaceCatalog)
    {
        string fileName = Path.GetFileName(relativePath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return content;
        }

        if (fileName.EndsWith("Repository.cs", StringComparison.OrdinalIgnoreCase)
            && !fileName.StartsWith("I", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(roots.RepositoryInterfacesNamespace)
                && content.Contains("I", StringComparison.Ordinal)
                && content.Contains("Repository", StringComparison.Ordinal)
                && !content.Contains($"using {roots.RepositoryInterfacesNamespace};", StringComparison.Ordinal))
            {
                return InsertUsingDirective(content, $"using {roots.RepositoryInterfacesNamespace};");
            }
        }

        content = EnsureReferencedTypeUsings(content, typeNamespaceCatalog);
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

        var usedTypeCandidates = Regex.Matches(content, @"\b([A-Z][A-Za-z0-9_]*)\b")
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .Where(typeName => !declaredTypes.Contains(typeName))
            .ToList();

        foreach (var typeName in usedTypeCandidates)
        {
            if (!catalog.TryGetUniqueNamespace(typeName, out string? ns) || string.IsNullOrWhiteSpace(ns))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(currentNamespace)
                && ns.Equals(currentNamespace, StringComparison.Ordinal))
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

    private static CanonicalRoots DetectCanonicalRoots(string repoPath)
    {
        string? webApiControllers = Directory
            .EnumerateDirectories(repoPath, "Controllers", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(repoPath, path).Replace('\\', '/'))
            .Where(relative => relative.Contains(".WebAPI/", StringComparison.OrdinalIgnoreCase)
                            || relative.Contains(".WebApi/", StringComparison.OrdinalIgnoreCase))
            .OrderBy(relative => relative.Length)
            .FirstOrDefault()
            ?? Directory
                .EnumerateDirectories(repoPath, "Controllers", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(repoPath, path).Replace('\\', '/'))
                .Where(relative => relative.Contains("WebAPI", StringComparison.OrdinalIgnoreCase))
                .OrderBy(relative => relative.Length)
                .FirstOrDefault();

        string? repositoryInterfaces = Directory
            .EnumerateDirectories(repoPath, "Interfaces", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(repoPath, path).Replace('\\', '/'))
            .Where(relative => relative.Contains(".Repository/", StringComparison.OrdinalIgnoreCase)
                            || relative.Contains("Repository/", StringComparison.OrdinalIgnoreCase))
            .OrderBy(relative => relative.Length)
            .FirstOrDefault();

        string? repositoryIndexes = Directory
            .EnumerateDirectories(repoPath, "Indexes", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(repoPath, path).Replace('\\', '/'))
            .Where(relative => relative.Contains(".Repository/", StringComparison.OrdinalIgnoreCase)
                            || relative.Contains("Repository/", StringComparison.OrdinalIgnoreCase))
            .OrderBy(relative => relative.Length)
            .FirstOrDefault()
            ?? Directory
                .EnumerateDirectories(repoPath, "Index", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(repoPath, path).Replace('\\', '/'))
                .Where(relative => relative.Contains(".Repository/", StringComparison.OrdinalIgnoreCase)
                                || relative.Contains("Repository/", StringComparison.OrdinalIgnoreCase))
                .OrderBy(relative => relative.Length)
                .FirstOrDefault();

        string? repositoryEntities = Directory
            .EnumerateDirectories(repoPath, "Entities", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(repoPath, path).Replace('\\', '/'))
            .Where(relative => relative.Contains(".Repository/", StringComparison.OrdinalIgnoreCase)
                            || relative.Contains("Repository/", StringComparison.OrdinalIgnoreCase))
            .OrderBy(relative => relative.Length)
            .FirstOrDefault();

        string? repositoryRoot = Directory
            .EnumerateFiles(repoPath, "*Repository.cs", SearchOption.AllDirectories)
            .Where(path => !Path.GetFileName(path).StartsWith("I", StringComparison.OrdinalIgnoreCase))
            .Where(path => !Path.GetFileName(path).Equals("Repository.cs", StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetRelativePath(repoPath, Path.GetDirectoryName(path) ?? string.Empty).Replace('\\', '/'))
            .Where(relative => relative.Contains(".Repository/", StringComparison.OrdinalIgnoreCase)
                            || relative.Contains("Repository/", StringComparison.OrdinalIgnoreCase))
            .OrderBy(relative => relative.Length)
            .FirstOrDefault();

        string? repositoryInterfacesNamespace = null;
        if (!string.IsNullOrWhiteSpace(repositoryInterfaces))
        {
            string interfacesAbsolute = Path.Combine(repoPath, repositoryInterfaces.Replace('/', Path.DirectorySeparatorChar));
            string? sampleInterfaceFile = Directory.EnumerateFiles(interfacesAbsolute, "I*Repository.cs", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(sampleInterfaceFile))
            {
                repositoryInterfacesNamespace = File.ReadLines(sampleInterfaceFile)
                    .Select(line => line.Trim())
                    .Where(line => line.StartsWith("namespace ", StringComparison.Ordinal))
                    .Select(line => line.Substring("namespace ".Length).Trim())
                    .FirstOrDefault();
            }
        }

        string? unitTestRepositoryTests = Directory
            .EnumerateDirectories(repoPath, "RepositoryTest", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(repoPath, path).Replace('\\', '/'))
            .Where(relative => relative.Contains(".UnitTest/", StringComparison.OrdinalIgnoreCase)
                            || relative.Contains("UnitTest/", StringComparison.OrdinalIgnoreCase))
            .OrderBy(relative => relative.Length)
            .FirstOrDefault();

        return new CanonicalRoots(
            webApiControllers,
            repositoryRoot,
            repositoryInterfaces,
            repositoryIndexes,
            repositoryEntities,
            repositoryInterfacesNamespace,
            unitTestRepositoryTests);
    }

    private static IEnumerable<GeneratedFile> EnumerateGeneratedFiles(WorkflowState state)
    {
        if (state.Stage == WorkflowStage.Recovering)
        {
            return state.Recovery?.ProposedFiles ?? Enumerable.Empty<GeneratedFile>();
        }

        if (state.Stage == WorkflowStage.Integrating)
        {
            return state.Recovery?.ProposedFiles ?? Enumerable.Empty<GeneratedFile>();
        }

        return (state.Backend?.ProposedFiles ?? Enumerable.Empty<GeneratedFile>())
            .Concat(state.Frontend?.ProposedFiles ?? Enumerable.Empty<GeneratedFile>());
    }

    private static string TryNormalizeRepositoryTestContent(string relativePath, string content, string repoPath)
    {
        string fileName = Path.GetFileName(relativePath);
        if (!fileName.EndsWith("RepositoryTests.cs", StringComparison.OrdinalIgnoreCase))
        {
            return content;
        }

        if (CSharpSyntaxGuard.TryValidate(content, out _))
        {
            return content;
        }

        string entity = Path.GetFileNameWithoutExtension(fileName);
        if (entity.EndsWith("RepositoryTests", StringComparison.Ordinal))
        {
            entity = entity[..^"RepositoryTests".Length];
        }

        if (!string.IsNullOrWhiteSpace(entity)
            && RepositoryTestTemplateBuilder.TryBuildFromExemplar(repoPath, entity, out string templated))
        {
            return templated;
        }

        return content;
    }

    private static bool IsLikelyValidSource(
        string relativePath,
        string content,
        bool isOverwritingExistingFile,
        string repoPath,
        LayerConventionProfiles conventions,
        InterfaceCatalog interfaceCatalog,
        out string reason)
    {
        reason = string.Empty;
        string extension = Path.GetExtension(relativePath).ToLowerInvariant();
        string trimmed = content.Trim();
        int lineCount = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).Length;

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            reason = "Generated content is empty.";
            return false;
        }

        if (trimmed.Length < 40)
        {
            reason = "Generated content is too short to be a real source file.";
            return false;
        }

        if (LooksLikePlainEnglishSentence(trimmed))
        {
            reason = "Generated content looks like prose, not source code.";
            return false;
        }

        if (trimmed.Contains("Corrected the", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("for frontend interaction", StringComparison.OrdinalIgnoreCase))
        {
            reason = "Generated content appears to be explanatory text, not code.";
            return false;
        }

        if (isOverwritingExistingFile && lineCount < 4)
        {
            reason = "Refused to overwrite existing file with very short output.";
            return false;
        }

        if (extension == ".cs")
        {
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

            string fileName = Path.GetFileName(relativePath);
            if (fileName.EndsWith("Repository.cs", StringComparison.OrdinalIgnoreCase)
                && !fileName.StartsWith("I", StringComparison.OrdinalIgnoreCase)
                && !fileName.Equals("Repository.cs", StringComparison.OrdinalIgnoreCase))
            {
                string entity = Path.GetFileNameWithoutExtension(fileName);
                if (entity.EndsWith("Repository", StringComparison.OrdinalIgnoreCase))
                {
                    entity = entity[..^"Repository".Length];
                }

                bool hasGenericBase = trimmed.Contains($"Repository<{entity}>", StringComparison.Ordinal);
                bool hasInterface = trimmed.Contains($"I{entity}Repository", StringComparison.Ordinal);
                bool hasDbStoreCtor = trimmed.Contains($"{entity}Repository(IDbStore dbStore)", StringComparison.Ordinal)
                                      && trimmed.Contains("base(dbStore)", StringComparison.Ordinal);
                if (!hasGenericBase || !hasInterface || !hasDbStoreCtor)
                {
                    reason = $"Repository contract failed for {fileName}: require Repository<{entity}> base, I{entity}Repository, and IDbStore ctor/base(dbStore).";
                    return false;
                }

                // Legacy fallback for repository projects that strongly follow generic repository shape.
                // Dynamic layer convention checks below handle broader architectures.
            }

            if (isOverwritingExistingFile
                && DependencyWiringAuditor.IsCompositionRootPath(relativePath))
            {
                string existingPath = Path.Combine(repoPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(existingPath)
                    && !DependencyWiringAuditor.TryValidateCompositionRootPreservation(
                        File.ReadAllText(existingPath),
                        trimmed,
                        out reason))
                {
                    return false;
                }
            }

            if (!ValidateDynamicLayerConventions(relativePath, trimmed, conventions, out reason))
            {
                return false;
            }

            if (!ValidateInterfaceContractParity(relativePath, trimmed, interfaceCatalog, out reason))
            {
                return false;
            }

            if (fileName.EndsWith("RepositoryTests.cs", StringComparison.OrdinalIgnoreCase)
                && !CSharpSyntaxGuard.TryValidate(trimmed, out string syntaxReason))
            {
                reason = $"Repository test syntax check failed: {syntaxReason}";
                return false;
            }
        }
        else if (extension is ".js" or ".ts" or ".tsx" or ".jsx")
        {
            bool hasScriptShape =
                trimmed.Contains("function", StringComparison.Ordinal)
                || trimmed.Contains("=>", StringComparison.Ordinal)
                || trimmed.Contains("class ", StringComparison.Ordinal)
                || trimmed.Contains("const ", StringComparison.Ordinal)
                || trimmed.Contains("let ", StringComparison.Ordinal)
                || trimmed.Contains("var ", StringComparison.Ordinal)
                || trimmed.Contains("angular.", StringComparison.OrdinalIgnoreCase);
            if (!hasScriptShape)
            {
                reason = "Script output missing expected function/class/module constructs.";
                return false;
            }
        }
        else if (extension == ".html")
        {
            if (!trimmed.Contains("<", StringComparison.Ordinal) || !trimmed.Contains(">", StringComparison.Ordinal))
            {
                reason = "HTML output missing markup tags.";
                return false;
            }
        }

        return true;
    }

    private static bool LooksLikePlainEnglishSentence(string content)
    {
        if (content.Contains("{", StringComparison.Ordinal)
            || content.Contains("}", StringComparison.Ordinal)
            || content.Contains(";", StringComparison.Ordinal))
        {
            return false;
        }

        return content.EndsWith(".", StringComparison.Ordinal)
               && content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).Length <= 2;
    }

    private static bool ValidateDynamicLayerConventions(
        string relativePath,
        string content,
        LayerConventionProfiles conventions,
        out string reason)
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

        foreach (var paramType in profile.RequiredConstructorParamTypes)
        {
            if (!content.Contains(paramType, StringComparison.Ordinal))
            {
                reason = $"Dynamic {role} contract failed for {fileName}: missing constructor dependency type '{paramType}'.";
                return false;
            }
        }

        return true;
    }

    private static bool ValidateInterfaceContractParity(
        string relativePath,
        string content,
        InterfaceCatalog interfaceCatalog,
        out string reason)
    {
        reason = string.Empty;
        string[] lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        string? classLine = lines
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith("public class ", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(classLine) || !classLine.Contains(":", StringComparison.Ordinal))
        {
            return true;
        }

        var declaredMethodNames = Regex.Matches(content, @"public\s+[A-Za-z0-9_<>\[\],\s\?]+\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(")
            .Select(m => m.Groups[1].Value)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.Ordinal);
        string className = Path.GetFileNameWithoutExtension(relativePath);
        declaredMethodNames.Remove(className);

        string inheritance = classLine.Split(':', 2)[1];
        var implementedInterfaces = inheritance
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim())
            .Where(token => token.StartsWith("I", StringComparison.Ordinal))
            .ToList();
        foreach (var iface in implementedInterfaces)
        {
            if (!interfaceCatalog.TryGetMethods(iface, out var interfaceMethods))
            {
                continue;
            }

            var missing = interfaceMethods.Where(method => !declaredMethodNames.Contains(method)).ToList();
            if (missing.Count > 0)
            {
                reason = $"Class {className} misses interface member implementation(s) from {iface}: {string.Join(", ", missing)}.";
                return false;
            }
        }

        var fieldTypeMap = Regex.Matches(content, @"(?:private|public|protected)\s+(?:readonly\s+)?(I[A-Za-z0-9_]+)\s+([A-Za-z_][A-Za-z0-9_]*)\s*;")
            .ToDictionary(
                m => m.Groups[2].Value,
                m => m.Groups[1].Value,
                StringComparer.Ordinal);

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

    private static InterfaceCatalog BuildInterfaceCatalog(string repoPath, IReadOnlyList<GeneratedFile> generatedFiles)
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
            AddInterfaceMethods(generated.Content, map, overwrite: true);
        }

        return new InterfaceCatalog(map);
    }

    private static TypeNamespaceCatalog BuildTypeNamespaceCatalog(string repoPath, IReadOnlyList<GeneratedFile> generatedFiles)
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

        var declarations = Regex.Matches(content, @"\b(class|interface|record|struct)\s+([A-Za-z_][A-Za-z0-9_]*)")
            .Select(match => match.Groups[2].Value)
            .Distinct(StringComparer.Ordinal);
        foreach (var type in declarations)
        {
            if (!map.TryGetValue(type, out var set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                map[type] = set;
            }
            set.Add(ns);
        }
    }

    private static void AddInterfaceMethods(string content, Dictionary<string, HashSet<string>> map, bool overwrite = false)
    {
        foreach (Match ifaceMatch in Regex.Matches(content, @"\binterface\s+(I[A-Za-z0-9_]*)\b"))
        {
            string iface = ifaceMatch.Groups[1].Value;
            if (overwrite || !map.ContainsKey(iface))
            {
                map[iface] = new HashSet<string>(StringComparer.Ordinal);
            }

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
        }
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

    private readonly record struct CanonicalRoots(
        string? WebApiControllers,
        string? RepositoryRoot,
        string? RepositoryInterfaces,
        string? RepositoryIndexes,
        string? RepositoryEntities,
        string? RepositoryInterfacesNamespace,
        string? UnitTestRepositoryTests);
}

readonly record struct ApplyIssue(string RelativePath, string Reason);

readonly record struct ApplyResult(
    IReadOnlyList<string> AppliedFiles,
    IReadOnlyList<ApplyIssue> RejectedFiles,
    IReadOnlyList<AppliedFileChange> AppliedChanges);

readonly record struct AppliedFileChange(
    string RelativePath,
    bool ExistedBeforeApply,
    string? PreviousContent);

sealed class InterfaceCatalog
{
    private readonly Dictionary<string, HashSet<string>> _methods;

    public InterfaceCatalog(Dictionary<string, HashSet<string>> methods)
    {
        _methods = methods;
    }

    public bool TryGetMethods(string interfaceName, out HashSet<string> methods)
    {
        return _methods.TryGetValue(interfaceName, out methods!);
    }
}

sealed class TypeNamespaceCatalog
{
    private readonly Dictionary<string, HashSet<string>> _namespacesByType;

    public TypeNamespaceCatalog(Dictionary<string, HashSet<string>> namespacesByType)
    {
        _namespacesByType = namespacesByType;
    }

    public bool IsEmpty => _namespacesByType.Count == 0;

    public bool TryGetUniqueNamespace(string typeName, out string? ns)
    {
        ns = null;
        if (!_namespacesByType.TryGetValue(typeName, out var namespaces))
        {
            return false;
        }

        if (namespaces.Count != 1)
        {
            return false;
        }

        ns = namespaces.FirstOrDefault();
        return !string.IsNullOrWhiteSpace(ns);
    }
}
