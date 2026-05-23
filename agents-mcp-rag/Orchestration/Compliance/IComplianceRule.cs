interface IComplianceRule
{
    string RuleId { get; }
    string Category { get; }
    bool AppliesTo(ComplianceContext context);
    IEnumerable<AgentFinding> Evaluate(ComplianceContext context);
}
