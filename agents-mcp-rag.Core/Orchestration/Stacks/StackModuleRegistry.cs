using agents_mcp_rag.Infrastructure;
using agents_mcp_rag.Orchestration.Compliance;

namespace agents_mcp_rag.Orchestration.Stacks;

/// <summary>
/// Registered stack plug-ins. Host registers modules at startup; tests call <see cref="Reset"/> then re-register.
/// </summary>
public static class StackModuleRegistry
{
    private static readonly List<IStackModule> Modules = [];
    private static readonly object Gate = new();

    public static bool IsInitialized
    {
        get
        {
            lock (Gate)
            {
                return Modules.Count > 0;
            }
        }
    }

    public static void Register(IStackModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        lock (Gate)
        {
            if (Modules.Any(existing => existing.StackId.Equals(module.StackId, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Stack module '{module.StackId}' is already registered.");
            }

            Modules.Add(module);
        }
    }

    public static void Reset()
    {
        lock (Gate)
        {
            Modules.Clear();
        }
    }

    public static IEnumerable<IStackModule> Active(RepoStack stack) =>
        Snapshot().Where(module => module.IsActive(stack));

    public static IEnumerable<IComplianceRule> ComplianceRules(RepoStack stack) =>
        Active(stack).SelectMany(module => module.ComplianceRules);

    public static IEnumerable<ITestReleasePolicy> TestReleasePolicies(RepoStack stack) =>
        Active(stack)
            .Select(module => module.TestReleasePolicy)
            .Where(policy => policy is not null)!;

    private static IReadOnlyList<IStackModule> Snapshot()
    {
        lock (Gate)
        {
            return Modules.ToList();
        }
    }
}
