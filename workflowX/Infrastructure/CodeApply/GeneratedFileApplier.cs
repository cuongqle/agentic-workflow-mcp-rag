namespace workflowX.Infrastructure;

static class GeneratedFileApplier
{
    public static Task<ApplyResult> ApplyAsync(WorkflowState state)
    {
        var ctx = ApplyContextFactory.Create(state);
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
            foreach (string repaired in CompositionRootMerger.RepairCompositionRootFiles(ctx.RepoPath))
            {
                applied.Add(repaired);
            }
        });

        return Task.FromResult(new ApplyResult(applied, rejected, appliedChanges));
    }

    public static Task RollbackAsync(string repoPath, IReadOnlyList<AppliedFileChange> changes) =>
        ApplyRollback.RollbackAsync(repoPath, changes);

    internal static IReadOnlyList<GeneratedFile> GetOrderedGeneratedFiles(WorkflowState state)
    {
        List<GeneratedFile> files = EnumerateGeneratedFiles(state).ToList();
        RepoStack stack = state.Contract?.Stack ?? RepoStack.None;
        return stack.DotNet
            ? CSharpApplySupport.OrderForApply(
                files,
                state.Contract?.LayerConventions ?? LayerConventionProfiles.Empty,
                state.Contract)
            : files.OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string NormalizeContent(ApplyContext ctx, string relativePath, string content)
    {
        if (ctx.Stack.DotNet && CSharpApplySupport.IsDotNetSourcePath(relativePath))
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

        if (!ApplyContentGuard.TryValidateCommonShape(content, existedBefore, out string commonReason))
        {
            reason = commonReason;
            return false;
        }

        if (!ArchitectureDeliverableScopeGuard.TryValidatePath(ctx.State, relativePath, ctx.Stack, out string scopeReason))
        {
            reason = scopeReason;
            return false;
        }

        if (CSharpApplySupport.IsDotNetSourcePath(relativePath))
        {
            if (!ctx.Stack.DotNet)
            {
                reason = "Rejected C# source file: repository has no .NET stack.";
                return false;
            }

            if (!CSharpApplySupport.TryValidateDotNet(
                    ctx, relativePath, content, existedBefore, existingOnDisk, ref validatedContent, out reason))
            {
                return false;
            }
        }
        else if (FrontendApplyGuard.IsFrontendSourcePath(relativePath))
        {
            if (!ctx.Stack.Frontend)
            {
                reason = "Rejected frontend source file: repository has no frontend stack.";
                return false;
            }

            if (!FrontendApplyGuard.TryValidateByExtension(relativePath, validatedContent, out reason))
            {
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

        relativePath = ctx.Stack.DotNetOr(
            DotNetRepoContractSupport.ResolveCanonicalRelativePath(ctx.Contract, relativePath, generatedFile.Content),
            ctx.Contract.ResolveCanonicalRelativePath(relativePath, generatedFile.Content));
        if (relativePath.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
            || relativePath.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
        {
            issue = "Rejected generated artifact path (obj/bin). Fix the owning project file instead.";
            return false;
        }

        string? declaredType = CSharpApplySupport.IsDotNetSourcePath(relativePath)
            ? CSharpApplySupport.TryExtractDeclaredTypeName(generatedFile.Content)
            : null;
        if (!string.IsNullOrWhiteSpace(declaredType)
            && ctx.DeclaredTypePaths.TryGetValue(declaredType, out string? priorPath))
        {
            relativePath = priorPath;
        }

        if (!TryResolveDuplicateToExistingPath(ctx, relativePath, generatedFile.Content, out relativePath, out issue))
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

    private static IEnumerable<GeneratedFile> EnumerateGeneratedFiles(WorkflowState state)
    {
        if (state.Stage is WorkflowStage.Recovering or WorkflowStage.Integrating)
        {
            return state.Recovery?.ProposedFiles ?? Enumerable.Empty<GeneratedFile>();
        }

        return (state.Backend?.ProposedFiles ?? Enumerable.Empty<GeneratedFile>())
            .Concat(state.Frontend?.ProposedFiles ?? Enumerable.Empty<GeneratedFile>());
    }

    private static bool TryResolveDuplicateToExistingPath(
        ApplyContext ctx,
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

        if (ctx.Stack.DotNet
            && fileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            && !CSharpApplySupport.TryResolveDeclaredTypePath(
                ctx.RepoPath, resolvedRelativePath, content, ref resolvedRelativePath, out issue))
        {
            return false;
        }

        bool isClassLike = fileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                           || fileName.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
                           || fileName.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)
                           || fileName.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase);
        if (!isClassLike)
        {
            return true;
        }

        var existingMatches = Directory.EnumerateFiles(ctx.RepoPath, fileName, SearchOption.AllDirectories)
            .Where(path => !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                        && !path.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
                        && !path.Contains("/node_modules/", StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetRelativePath(ctx.RepoPath, path).Replace('\\', '/'))
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
                string absolute = Path.Combine(ctx.RepoPath, path.Replace('/', Path.DirectorySeparatorChar));
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

        // Fall back to the closest directory match for duplicate file names
        // (e.g. Program.cs in multiple projects). This avoids ambiguous rejections
        // when the proposed relative path already carries useful folder context.
        var scoredMatches = existingMatches
            .Select(path => new
            {
                Path = path,
                Score = ComputePathOverlapScore(resolvedPath, path)
            })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (scoredMatches.Count > 0
            && scoredMatches[0].Score > 0
            && (scoredMatches.Count == 1 || scoredMatches[0].Score > scoredMatches[1].Score))
        {
            resolvedRelativePath = scoredMatches[0].Path;
            return true;
        }

        issue = $"Ambiguous duplicate target for '{fileName}'. Existing candidates: {string.Join(", ", existingMatches)}";
        return false;
    }

    private static int ComputePathOverlapScore(string proposedRelativePath, string existingRelativePath)
    {
        string proposedDir = (Path.GetDirectoryName(proposedRelativePath) ?? string.Empty).Replace('\\', '/');
        string existingDir = (Path.GetDirectoryName(existingRelativePath) ?? string.Empty).Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(proposedDir) || string.IsNullOrWhiteSpace(existingDir))
        {
            return 0;
        }

        string[] proposedSegments = proposedDir.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string[] existingSegments = existingDir.Split('/', StringSplitOptions.RemoveEmptyEntries);
        int i = proposedSegments.Length - 1;
        int j = existingSegments.Length - 1;
        int score = 0;
        while (i >= 0 && j >= 0)
        {
            if (!proposedSegments[i].Equals(existingSegments[j], StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            score++;
            i--;
            j--;
        }

        return score;
    }
}
