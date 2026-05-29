namespace workflowX.Infrastructure;

/// <summary>
/// Rejects generated files that are not listed in the architecture plan deliverables.
/// </summary>
internal static class ArchitectureDeliverableScopeGuard
{
    internal static bool TryValidatePath(
        WorkflowState state,
        string relativePath,
        RepoStack stack,
        out string reason)
    {
        reason = string.Empty;
        if (state.Stage is WorkflowStage.Recovering or WorkflowStage.Integrating)
        {
            return true;
        }

        var allowed = new List<string>();
        if (stack.DotNet)
        {
            allowed.AddRange(ArchitectureDeliverableMatcher.BuildBackendAllowedPaths(state));
        }

        if (stack.Frontend)
        {
            allowed.AddRange(ArchitectureDeliverableMatcher.BuildFrontendAllowedPaths(state));
        }

        if (allowed.Count == 0)
        {
            return true;
        }

        if (ArchitectureDeliverableMatcher.IsStrictArchitectureDeliverable(relativePath, allowed, state.Contract))
        {
            return true;
        }

        reason =
            $"File '{relativePath}' is not listed in the architecture plan (BACKEND_FILES/FRONTEND_FILES). "
            + "Return only paths that match the plan exactly (no extra repository or solution folder prefix).";
        return false;
    }
}
