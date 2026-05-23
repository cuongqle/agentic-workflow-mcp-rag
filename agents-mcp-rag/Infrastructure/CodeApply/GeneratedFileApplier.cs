using System.Text.RegularExpressions;

namespace agents_mcp_rag.Infrastructure;

static class GeneratedFileApplier
{
    public static Task<ApplyResult> ApplyAsync(WorkflowState state)
    {
        var ctx = ApplyContext.Create(state);
        var applied = new List<string>();
        var rejected = new List<ApplyIssue>();
        var appliedChanges = new List<AppliedFileChange>();

        foreach (var generatedFile in ctx.GeneratedFiles)
        {
            if (!TryPrepare(ctx, generatedFile, out string relativePath, out string fullPath, out bool existedBefore, out string? existingOnDisk, out string? prepareIssue))
            {
                if (!string.IsNullOrWhiteSpace(prepareIssue))
                {
                    string path = generatedFile.RelativePath.Replace('\\', '/').Trim();
                    rejected.Add(new ApplyIssue(path, prepareIssue));
                }

                continue;
            }

            string content = NormalizeContent(ctx, relativePath, generatedFile.Content);

            if (!TryValidate(ctx, relativePath, content, existedBefore, existingOnDisk, out content, out string? validateReason))
            {
                rejected.Add(new ApplyIssue(relativePath, validateReason!));
                continue;
            }

            string? previousContent = existingOnDisk;
            File.WriteAllText(fullPath, content);
            applied.Add(relativePath);
            appliedChanges.Add(new AppliedFileChange(relativePath, existedBefore, previousContent));
        }

        foreach (string autoApplied in ctx.Stack.WhenDotNet(
            DependencyWiringAuditor.ApplyMissingRegistrations(state)))
        {
            applied.Add(autoApplied);
        }

        ctx.Stack.WhenDotNet(() =>
        {
            foreach (string repaired in RepairCompositionRootFiles(ctx.RepoPath))
            {
                applied.Add(repaired);
            }
        });

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

    internal static IReadOnlyList<GeneratedFile> GetOrderedGeneratedFiles(WorkflowState state) =>
        OrderFilesForApply(EnumerateGeneratedFiles(state).ToList());

    private static string NormalizeContent(ApplyContext ctx, string relativePath, string content)
    {
        if (ctx.Stack.DotNet && relativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return CSharpApplySupport.Normalize(ctx, relativePath, content);
        }

        return content;
    }

    private static bool TryValidate(
        ApplyContext ctx,
        string relativePath,
        string content,
        bool existedBefore,
        string? existingOnDisk,
        out string validatedContent,
        out string? reason)
    {
        validatedContent = content;
        reason = null;

        if (!CSharpApplySupport.TryValidateCommonShape(content, existedBefore, out reason))
        {
            return false;
        }

        if (ctx.Stack.DotNet && !TryValidateDotNet(ctx, relativePath, content, existedBefore, existingOnDisk, ref validatedContent, out reason))
        {
            return false;
        }

        return TryValidateByExtension(relativePath, validatedContent, out reason);
    }

    private static bool TryValidateDotNet(
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
            && !CSharpApplySupport.TryValidate(ctx, relativePath, content, existedBefore, out reason))
        {
            return false;
        }

        return true;
    }

    private static bool TryValidateByExtension(string relativePath, string content, out string? reason)
    {
        reason = null;
        string extension = Path.GetExtension(relativePath).ToLowerInvariant();

        if (extension is ".js" or ".ts" or ".tsx" or ".jsx")
        {
            string trimmed = content.Trim();
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
            string trimmed = content.Trim();
            if (!trimmed.Contains("<", StringComparison.Ordinal) || !trimmed.Contains(">", StringComparison.Ordinal))
            {
                reason = "HTML output missing markup tags.";
                return false;
            }
        }

        return true;
    }

    private static bool TryPrepare(
        ApplyContext ctx,
        GeneratedFile generatedFile,
        out string relativePath,
        out string fullPath,
        out bool existedBefore,
        out string? existingOnDisk,
        out string? issue)
    {
        relativePath = string.Empty;
        fullPath = string.Empty;
        existedBefore = false;
        existingOnDisk = null;
        issue = null;

        relativePath = generatedFile.RelativePath.Replace('\\', '/').Trim();
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            return false;
        }

        relativePath = ctx.Contract.ResolveCanonicalRelativePath(relativePath, generatedFile.Content);
        if (relativePath.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
            || relativePath.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
        {
            issue = "Rejected generated artifact path (obj/bin). Fix the owning .csproj or Properties/AssemblyInfo.cs instead.";
            return false;
        }

        string? declaredType = TryExtractDeclaredTypeName(generatedFile.Content);
        if (!string.IsNullOrWhiteSpace(declaredType)
            && ctx.DeclaredTypePaths.TryGetValue(declaredType, out string? priorPath))
        {
            relativePath = priorPath;
        }

        if (!TryResolveDuplicateToExistingPath(ctx.RepoPath, relativePath, generatedFile.Content, out relativePath, out issue))
        {
            issue ??= "Duplicate resolution failed.";
            return false;
        }

        relativePath = relativePath.TrimStart('/');
        if (!string.IsNullOrWhiteSpace(declaredType))
        {
            ctx.DeclaredTypePaths[declaredType] = relativePath;
        }

        fullPath = Path.GetFullPath(Path.Combine(ctx.RepoPath, relativePath));
        if (!fullPath.StartsWith(ctx.RepoRoot, StringComparison.OrdinalIgnoreCase))
        {
            issue = "Rejected path outside repository root.";
            return false;
        }

        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        existedBefore = File.Exists(fullPath);
        existingOnDisk = existedBefore ? File.ReadAllText(fullPath) : null;
        return true;
    }

    private static IEnumerable<string> RepairCompositionRootFiles(string repoPath)
    {
        foreach (string path in Directory.EnumerateFiles(repoPath, "*.cs", SearchOption.AllDirectories))
        {
            if (path.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                || path.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string relative = Path.GetRelativePath(repoPath, path).Replace('\\', '/');
            if (!DependencyWiringAuditor.IsCompositionRootPath(relative))
            {
                continue;
            }

            string original = File.ReadAllText(path);
            string sanitized = CompositionRootMerger.SanitizeBootstrapContent(original);
            if (!sanitized.Equals(original, StringComparison.Ordinal)
                && CompositionRootMerger.PassesBootstrapSyntaxChecks(sanitized, out _))
            {
                File.WriteAllText(path, sanitized);
                yield return $"repaired bootstrap: {relative}";
            }
        }
    }

    private static IEnumerable<GeneratedFile> EnumerateGeneratedFiles(WorkflowState state)
    {
        if (state.Stage is WorkflowStage.Recovering or WorkflowStage.Integrating)
        {
            return state.Recovery?.ProposedFiles ?? Enumerable.Empty<GeneratedFile>();
        }

        return (state.Backend?.ProposedFiles ?? Enumerable.Empty<GeneratedFile>())
            .Concat(state.Frontend?.ProposedFiles ?? Enumerable.Empty<GeneratedFile>());
    }

    private static List<GeneratedFile> OrderFilesForApply(IReadOnlyList<GeneratedFile> files) =>
        files
            .OrderBy(f => GetApplyPriority(f.RelativePath))
            .ThenBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static int GetApplyPriority(string relativePath)
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

    private static string? TryExtractDeclaredTypeName(string content)
    {
        Match match = Regex.Match(
            content,
            @"\bpublic\s+(?:partial\s+)?(?:class|interface)\s+([A-Za-z_][A-Za-z0-9_]*)\b");
        return match.Success ? match.Groups[1].Value : null;
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

    private static bool TryResolveDuplicateToExistingPath(
        string repoPath,
        string relativePath,
        string content,
        out string resolvedRelativePath,
        out string? issue)
    {
        resolvedRelativePath = relativePath.Replace('\\', '/').TrimStart('/');
        issue = null;
        string targetPath = resolvedRelativePath;
        string fileName = Path.GetFileName(targetPath);
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

        string? declaredType = TryExtractDeclaredTypeName(content);
        if (!string.IsNullOrWhiteSpace(declaredType))
        {
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

        string resolvedPath = resolvedRelativePath;
        if (existingMatches.Any(match => match.Equals(resolvedPath, StringComparison.OrdinalIgnoreCase)))
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
}
