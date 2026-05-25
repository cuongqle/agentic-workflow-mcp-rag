using workflowX.Infrastructure;

public enum WorkflowStage
{
    Queued,
    Requirements,
    Planning,
    Implementing,
    Integrating,
    Auditing,
    Recovering,
    ValidatingAcceptance,
    ReadyForPR,
    Done,
    Blocked
}

public enum FindingSeverity
{
    Low,
    Medium,
    High,
    Blocker
}

public sealed class WorkflowTask
{
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}

public sealed class AgentFinding
{
    public FindingSeverity Severity { get; init; } = FindingSeverity.Low;
    public string Message { get; init; } = string.Empty;
}

public sealed class AgentResult
{
    public string AgentName { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public ArchitecturePlan? ArchitecturePlan { get; init; }
    public RequirementsSpec? RequirementsSpec { get; init; }
    public AcceptanceCriteriaReport? AcceptanceCriteriaReport { get; init; }
    public List<GeneratedFile> ProposedFiles { get; init; } = new();
    public List<AgentFinding> Findings { get; init; } = new();
    public bool? ProductionBuildPassed { get; init; }
    public bool? TestsPassed { get; init; }
}

public sealed class GeneratedFile
{
    public string RelativePath { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
}

public sealed class WorkflowState
{
    public WorkflowTask Task { get; set; } = new();
    public string RepoPath { get; set; } = string.Empty;
    public string ProjectStructureContext { get; set; } = string.Empty;
    public string LegacyImplementationContext { get; set; } = string.Empty;
    public string CombinedRagContext { get; set; } = string.Empty;
    public RepoContract? Contract { get; set; }
    public WorkflowStage Stage { get; set; } = WorkflowStage.Queued;
    public int RecoveryAttemptCount { get; set; }

    public AgentResult? Requirements { get; set; }
    public RequirementsSpec? RequirementsSpec { get; set; }
    public AgentResult? Architecture { get; set; }
    public ArchitecturePlan? ArchitecturePlan { get; set; }
    public AgentResult? AcceptanceCriteria { get; set; }
    public AgentResult? Observer { get; set; }
    public AgentResult? Backend { get; set; }
    public AgentResult? Frontend { get; set; }
    public AgentResult? BuildValidation { get; set; }
    public AgentResult? Audit { get; set; }
    public AgentResult? Recovery { get; set; }
    public List<string> CompilationFixAllowedFiles { get; set; } = new();
    public string CompilationFixExemplarContext { get; set; } = string.Empty;
    public List<string> ComplianceIssues { get; set; } = new();
    public HashSet<string> DeferredTestEntities { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string? PullRequestUrl { get; set; }
    public string? PullRequestStatus { get; set; }
    public List<string> AppliedFiles { get; } = new();

    public List<string> Timeline { get; } = new();

    public void AddTimeline(string message)
    {
        Timeline.Add($"{DateTimeOffset.UtcNow:u} | {message}");
    }
}
