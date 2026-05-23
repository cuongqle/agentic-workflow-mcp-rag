static class ContractComplianceValidator
{
    public static List<AgentFinding> CollectComplianceFindings(WorkflowState state)
    {
        var context = ComplianceContext.Create(state);
        return ComplianceRuleRegistry.For(context)
            .Where(rule => rule.AppliesTo(context))
            .SelectMany(rule => rule.Evaluate(context))
            .ToList();
    }
}
