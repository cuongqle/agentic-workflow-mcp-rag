static class RollbackTracker
{
    public static void CaptureRollbackChanges(
        Dictionary<string, AppliedFileChange> rollbackChanges,
        IReadOnlyList<AppliedFileChange> currentChanges)
    {
        foreach (var change in currentChanges)
        {
            if (!rollbackChanges.ContainsKey(change.RelativePath))
            {
                rollbackChanges[change.RelativePath] = change;
            }
        }
    }
}
