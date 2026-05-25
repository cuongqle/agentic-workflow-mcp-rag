using workflowX.Infrastructure;

sealed class ProtectedContractComplianceRule : FileComplianceRule
{
    public override string RuleId => "contract.protected-overwrite";
    public override string Category => "contract";

    protected override bool ShouldInspect(GeneratedFile file, ComplianceContext context) => true;

    protected override AgentFinding? ValidateFile(GeneratedFile file, ComplianceContext context)
    {
        string relative = file.RelativePath.Replace('\\', '/');
        string absolute = Path.Combine(context.RepoPath, relative.Replace('/', Path.DirectorySeparatorChar));
        string? existing = File.Exists(absolute) ? File.ReadAllText(absolute) : null;

        if (PreExistingContractGuard.TryValidateOverwrite(
                relative,
                existing,
                file.Content,
                context.ProposedPaths,
                out string reason))
        {
            return null;
        }

        return new AgentFinding
        {
            Severity = FindingSeverity.Blocker,
            Message = reason
        };
    }
}
