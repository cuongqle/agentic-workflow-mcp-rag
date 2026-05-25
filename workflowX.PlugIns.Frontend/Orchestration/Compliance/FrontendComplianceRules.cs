using workflowX.Infrastructure;

/// <summary>
/// Frontend (JS/TS/HTML) compliance rules — only included when <see cref="RepoStack.Frontend"/>.
/// </summary>
static class FrontendComplianceRules
{
    public static IReadOnlyList<IComplianceRule> All { get; } =
    [
        new FrontendPathConventionComplianceRule()
    ];
}

sealed class FrontendPathConventionComplianceRule : IComplianceRule
{
    public string RuleId => "architecture.frontend-path-conventions";
    public string Category => "architecture";

    public bool AppliesTo(ComplianceContext context) =>
        context.Stack.Frontend && context.ProposedFiles.Count > 0;

    public IEnumerable<AgentFinding> Evaluate(ComplianceContext context) =>
        context.Contract.CollectFrontendFindings(context.ProposedFiles);
}
