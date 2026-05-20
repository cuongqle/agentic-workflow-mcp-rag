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
               || finding.Message.StartsWith("Composition root must keep existing registration", StringComparison.OrdinalIgnoreCase)
               || finding.Message.StartsWith("Test bootstrap must not replace InMemory", StringComparison.OrdinalIgnoreCase)
               || finding.Message.StartsWith("Refused to change existing interface", StringComparison.OrdinalIgnoreCase)
               || finding.Message.StartsWith("Refused to modify pre-existing infrastructure", StringComparison.OrdinalIgnoreCase)
               || finding.Message.StartsWith("Refused to generate or redefine protected interface", StringComparison.OrdinalIgnoreCase)
               || finding.Message.Contains("forbidden infrastructure API", StringComparison.OrdinalIgnoreCase)
               || (finding.Message.Contains("must not access", StringComparison.OrdinalIgnoreCase)
                   && finding.Message.Contains("ServiceProvider", StringComparison.OrdinalIgnoreCase))
               || finding.Message.StartsWith("Missing unit test for ", StringComparison.OrdinalIgnoreCase)
               || (finding.Message.StartsWith("Missing ", StringComparison.OrdinalIgnoreCase)
                   && finding.Message.Contains(" interface for ", StringComparison.OrdinalIgnoreCase))
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
