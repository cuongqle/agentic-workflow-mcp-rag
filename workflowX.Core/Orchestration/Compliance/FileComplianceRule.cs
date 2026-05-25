using workflowX.Infrastructure;

namespace workflowX.Orchestration.Compliance;

public abstract class FileComplianceRule : IComplianceRule
{
    public abstract string RuleId { get; }
    public abstract string Category { get; }

    public virtual bool AppliesTo(ComplianceContext context) => context.ProposedFiles.Count > 0;

    protected abstract bool ShouldInspect(GeneratedFile file, ComplianceContext context);

    protected abstract AgentFinding? ValidateFile(GeneratedFile file, ComplianceContext context);

    public IEnumerable<AgentFinding> Evaluate(ComplianceContext context)
    {
        foreach (GeneratedFile file in context.ProposedFiles)
        {
            if (!ShouldInspect(file, context))
            {
                continue;
            }

            AgentFinding? finding = ValidateFile(file, context);
            if (finding is not null)
            {
                yield return finding;
            }
        }
    }
}
