using agents_mcp_rag.Infrastructure;

namespace agents_mcp_rag.Orchestration.Compliance;

public interface IComplianceRule
{
    string RuleId { get; }
    string Category { get; }
    bool AppliesTo(ComplianceContext context);
    IEnumerable<AgentFinding> Evaluate(ComplianceContext context);
}
