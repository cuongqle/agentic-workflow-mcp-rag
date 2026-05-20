static class WorkflowFindingRules
{
    public static List<GeneratedFile> GetAllProposedFiles(WorkflowState state)
    {
        return (state.Backend?.ProposedFiles ?? new List<GeneratedFile>())
            .Concat(state.Recovery?.ProposedFiles ?? new List<GeneratedFile>())
            .ToList();
    }

    public static bool HasBlockingFindings(IEnumerable<AgentFinding> findings)
    {
        return findings.Any(f => f.Severity is FindingSeverity.High or FindingSeverity.Blocker);
    }

    public static bool IsAutomatedComplianceFinding(AgentFinding finding)
    {
        return finding.Message.StartsWith("Missing DI registration for ", StringComparison.OrdinalIgnoreCase)
               || finding.Message.StartsWith("Missing unit test for ", StringComparison.OrdinalIgnoreCase)
               || finding.Message.StartsWith("Missing repository interface for ", StringComparison.OrdinalIgnoreCase)
               || finding.Message.StartsWith("Interface ", StringComparison.OrdinalIgnoreCase)
               || finding.Message.StartsWith("Duplicate index file detected", StringComparison.OrdinalIgnoreCase)
               || finding.Message.Contains("should implement I", StringComparison.OrdinalIgnoreCase)
               || finding.Message.Contains("should include", StringComparison.OrdinalIgnoreCase)
               || finding.Message.StartsWith("Rejected generated file ", StringComparison.OrdinalIgnoreCase)
               || finding.Message.StartsWith("Rejected recovery file ", StringComparison.OrdinalIgnoreCase)
               || finding.Message.StartsWith("Deferred test gate:", StringComparison.OrdinalIgnoreCase)
               || finding.Message.StartsWith("Deferred unit test generation for ", StringComparison.OrdinalIgnoreCase);
    }
}
