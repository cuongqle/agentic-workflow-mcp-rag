using workflowX.Infrastructure;

static class ComplianceRuleRegistry
{
    private static IReadOnlyList<IComplianceRule> Shared { get; } =
    [
        new ArchitectureCoverageComplianceRule()
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
