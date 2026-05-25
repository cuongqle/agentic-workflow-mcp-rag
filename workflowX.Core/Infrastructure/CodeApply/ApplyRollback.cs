namespace workflowX.Infrastructure;

/// <summary>
/// Stack-agnostic file rollback used by apply and stack test-release policies.
/// </summary>
public static class ApplyRollback
{
    public static Task RollbackAsync(string repoPath, IReadOnlyList<AppliedFileChange> changes)
    {
        string repoRoot = Path.GetFullPath(repoPath);
        foreach (AppliedFileChange change in changes.Reverse())
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
}
