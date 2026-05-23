using agents_mcp_rag.Infrastructure;

static class ComplianceRuleRegistry
{
    private static IReadOnlyList<IComplianceRule> Shared { get; } =
    [
        new ArchitectureCoverageComplianceRule(),
        new ProtectedContractComplianceRule()
    ];

    public static IReadOnlyList<IComplianceRule> For(ComplianceContext context) =>
        For(context.Stack);

    public static IReadOnlyList<IComplianceRule> For(RepoStack stack)
    {
        StackModuleRegistration.RegisterDefaults();

        var rules = new List<IComplianceRule>(Shared);
        rules.AddRange(StackModuleRegistry.ComplianceRules(stack));
        return rules;
    }
}
