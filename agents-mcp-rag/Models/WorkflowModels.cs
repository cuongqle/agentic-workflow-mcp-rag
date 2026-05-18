enum WorkflowStage
{
    Queued,
    Planning,
    Implementing,
    Integrating,
    Auditing,
    Recovering,
    ReadyForPR,
    Done,
    Blocked
}

enum FindingSeverity
{
    Low,
    Medium,
    High,
    Blocker
}

sealed class WorkflowTask
{
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}

sealed class AgentFinding
{
    public FindingSeverity Severity { get; init; } = FindingSeverity.Low;
    public string Message { get; init; } = string.Empty;
}

sealed class AgentResult
{
    public string AgentName { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public List<GeneratedFile> ProposedFiles { get; init; } = new();
    public List<AgentFinding> Findings { get; init; } = new();
    public bool? ProductionBuildPassed { get; init; }
}

sealed class GeneratedFile
{
    public string RelativePath { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
}

sealed class WorkflowState
{
    public WorkflowTask Task { get; init; } = new();
    public string RepoPath { get; init; } = string.Empty;
    public string ProjectStructureContext { get; init; } = string.Empty;
    public string LegacyImplementationContext { get; init; } = string.Empty;
    public string CombinedRagContext { get; init; } = string.Empty;
    public WorkflowStage Stage { get; set; } = WorkflowStage.Queued;
    public int RecoveryAttemptCount { get; set; }

    public AgentResult? Architecture { get; set; }
    public AgentResult? Observer { get; set; }
    public AgentResult? Backend { get; set; }
    public AgentResult? Frontend { get; set; }
    public AgentResult? BuildValidation { get; set; }
    public AgentResult? Audit { get; set; }
    public AgentResult? Recovery { get; set; }
    public List<string> CompilationFixAllowedFiles { get; set; } = new();
    public List<string> ComplianceIssues { get; set; } = new();
    public HashSet<string> DeferredTestEntities { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string CompilationContractContext { get; set; } = string.Empty;
    public string? PullRequestUrl { get; set; }
    public string? PullRequestStatus { get; set; }

    public List<string> Timeline { get; } = new();

    public void AddTimeline(string message)
    {
        Timeline.Add($"{DateTimeOffset.UtcNow:u} | {message}");
    }
}
