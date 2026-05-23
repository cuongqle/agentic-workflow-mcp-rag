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
        var rules = new List<IComplianceRule>(Shared);
        stack.WhenFrontend(() => rules.AddRange(FrontendComplianceRules.All));
        stack.WhenDotNet(() => rules.AddRange(DotNetComplianceRules.All));
        return rules;
    }
}
