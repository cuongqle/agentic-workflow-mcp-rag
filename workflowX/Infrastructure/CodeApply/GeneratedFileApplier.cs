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

            string content = generatedFile.Content;

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

        return Task.FromResult(new ApplyResult(applied, rejected, appliedChanges));
    }

    public static Task RollbackAsync(string repoPath, IReadOnlyList<AppliedFileChange> changes) =>
        ApplyRollback.RollbackAsync(repoPath, changes);

    internal static IReadOnlyList<GeneratedFile> GetGeneratedFiles(WorkflowState state) =>
        EnumerateGeneratedFiles(state).ToList();

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

        if (!RecoveryApplySupport.TryValidateDotNetRecoveryOverwrite(
                ctx.State,
                ctx.Stack,
                ctx.RepoPath,
                relativePath,
                existedBefore,
                out string overwriteReason))
        {
            reason = overwriteReason;
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

            if (!CSharpApplySupport.TryValidateDotNet(relativePath, validatedContent, out reason))
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

        relativePath = relativePath.TrimStart('/');
        if (!TryResolveRecoveryRelativePath(ctx, ref relativePath, out issue))
        {
            return false;
        }

        if (relativePath.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
            || relativePath.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
        {
            issue = "Rejected generated artifact path (obj/bin). Fix the owning project file instead.";
            return false;
        }

        if (CSharpAssemblyMetadataGuard.IsAssemblyInfoPath(relativePath))
        {
            issue =
                "Rejected AssemblyInfo.cs — SDK projects auto-generate assembly attributes. "
                + "Remove [assembly: Assembly*] metadata from generated output.";
            return false;
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

    private static bool TryResolveRecoveryRelativePath(
        ApplyContext ctx,
        ref string relativePath,
        out string? issue)
    {
        issue = null;
        if (ctx.State.Stage is not (WorkflowStage.Recovering or WorkflowStage.Integrating))
        {
            return true;
        }

        string canonicalPath = RecoveryPathSupport.CanonicalizeRecoveryPath(relativePath);
        if (!canonicalPath.Equals(relativePath, StringComparison.OrdinalIgnoreCase))
        {
            relativePath = canonicalPath;
        }

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

}
