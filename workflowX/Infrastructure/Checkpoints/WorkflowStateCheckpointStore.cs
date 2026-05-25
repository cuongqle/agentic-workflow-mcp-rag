using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using workflowX.Configuration;

namespace workflowX.Infrastructure;

internal sealed class WorkflowStateCheckpointData
{
    public int Version { get; set; } = WorkflowStateCheckpointStore.CurrentVersion;
    public string TaskTitle { get; set; } = string.Empty;
    public string TaskDescription { get; set; } = string.Empty;
    public string TaskHash { get; set; } = string.Empty;
    public string RepoPath { get; set; } = string.Empty;
    public WorkflowStage Stage { get; set; } = WorkflowStage.Queued;
    public int RecoveryAttemptCount { get; set; }
    public string ProjectStructureContext { get; set; } = string.Empty;
    public string LegacyImplementationContext { get; set; } = string.Empty;
    public string CombinedRagContext { get; set; } = string.Empty;
    public AgentResultData? Requirements { get; set; }
    public RequirementsSpec? RequirementsSpec { get; set; }
    public AgentResultData? Architecture { get; set; }
    public ArchitecturePlan? ArchitecturePlan { get; set; }
    public AgentResultData? AcceptanceCriteria { get; set; }
    public AgentResultData? Observer { get; set; }
    public AgentResultData? Backend { get; set; }
    public AgentResultData? Frontend { get; set; }
    public AgentResultData? BuildValidation { get; set; }
    public AgentResultData? Audit { get; set; }
    public AgentResultData? Recovery { get; set; }
    public List<string> CompilationFixAllowedFiles { get; set; } = new();
    public string CompilationFixExemplarContext { get; set; } = string.Empty;
    public List<string> ComplianceIssues { get; set; } = new();
    public List<string> DeferredTestEntities { get; set; } = new();
    public string? PullRequestUrl { get; set; }
    public string? PullRequestStatus { get; set; }
    public List<string> AppliedFiles { get; set; } = new();
    public List<string> Timeline { get; set; } = new();
}

internal sealed class AgentResultData
{
    public string AgentName { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public ArchitecturePlan? ArchitecturePlan { get; set; }
    public RequirementsSpec? RequirementsSpec { get; set; }
    public AcceptanceCriteriaReport? AcceptanceCriteriaReport { get; set; }
    public List<GeneratedFileData> ProposedFiles { get; set; } = new();
    public List<AgentFindingData> Findings { get; set; } = new();
    public bool? ProductionBuildPassed { get; set; }
    public bool? TestsPassed { get; set; }
}

internal sealed class GeneratedFileData
{
    public string RelativePath { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

internal sealed class AgentFindingData
{
    public FindingSeverity Severity { get; set; } = FindingSeverity.Low;
    public string Message { get; set; } = string.Empty;
}

internal static class WorkflowStateCheckpointStore
{
    public const int CurrentVersion = 1;
    public const string DefaultFileName = "workflow-state.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static string DefaultCheckpointPath(string repoPath) =>
        Path.Combine(WorkflowArtifactWriter.OutputDirectory(new WorkflowState { RepoPath = repoPath }), DefaultFileName);

    public static string ComputeTaskHash(string taskDescription) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(taskDescription.Trim().ToLowerInvariant())));

    public static void Save(WorkflowState state, string? checkpointPath = null)
    {
        string path = ResolvePath(state.RepoPath, checkpointPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        WorkflowStateCheckpointData data = ToData(state);
        string json = JsonSerializer.Serialize(data, JsonOptions);
        File.WriteAllText(path, json);
    }

    public static WorkflowState? TryLoad(
        string repoPath,
        string taskDescription,
        WorkflowResumeOptions options)
    {
        if (!options.ResumeFromCheckpoint)
        {
            return null;
        }

        string checkpointPath = ResolvePath(repoPath, options.CheckpointPath);
        if (File.Exists(checkpointPath))
        {
            WorkflowState? fromCheckpoint = TryLoadFile(checkpointPath, repoPath, taskDescription, options.StartFromStage);
            if (fromCheckpoint is not null)
            {
                return fromCheckpoint;
            }
        }

        if (options.StartFromStage is not null)
        {
            return null;
        }

        return TryLoadFromArtifacts(repoPath, taskDescription);
    }

    public static WorkflowState? TryLoadFile(
        string checkpointPath,
        string repoPath,
        string taskDescription,
        WorkflowStage? explicitStartStage = null)
    {
        if (!File.Exists(checkpointPath))
        {
            return null;
        }

        try
        {
            string json = File.ReadAllText(checkpointPath);
            WorkflowStateCheckpointData? data = JsonSerializer.Deserialize<WorkflowStateCheckpointData>(json, JsonOptions);
            if (data is null || data.Version != CurrentVersion)
            {
                return null;
            }

            string expectedHash = ComputeTaskHash(taskDescription);
            if (!string.IsNullOrWhiteSpace(data.TaskHash)
                && !data.TaskHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("[Checkpoint] Task changed since last run; ignoring saved workflow state.");
                return null;
            }

            WorkflowState state = FromData(data, repoPath);
            if (explicitStartStage is WorkflowStage startStage)
            {
                state.Stage = startStage;
            }

            return state;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[Checkpoint] Failed to load workflow state: {ex.Message}");
            return null;
        }
    }

    public static WorkflowState? TryLoadFromArtifacts(string repoPath, string taskDescription)
    {
        string outputDir = WorkflowArtifactWriter.OutputDirectory(new WorkflowState { RepoPath = repoPath });
        string requirementsPath = Path.Combine(outputDir, "requirements.json");
        string architecturePath = Path.Combine(outputDir, "architecture-plan.json");
        if (!File.Exists(requirementsPath) || !File.Exists(architecturePath))
        {
            return null;
        }

        string requirementsJson = File.ReadAllText(requirementsPath);
        string architectureJson = File.ReadAllText(architecturePath);
        if (!RequirementsSpecParser.TryParseJson(requirementsJson, out RequirementsSpec? requirementsSpec, out string requirementsSummary)
            || requirementsSpec is null
            || !ArchitecturePlanParser.TryParseJson(architectureJson, out ArchitecturePlan? architecturePlan, out string architectureSummary)
            || architecturePlan is null)
        {
            return null;
        }

        Console.WriteLine("[Checkpoint] Loaded requirements and architecture artifacts; resuming at Implementing.");
        return new WorkflowState
        {
            RepoPath = repoPath,
            Stage = WorkflowStage.Implementing,
            Task = new WorkflowTask
            {
                Title = "Resumed Development Task",
                Description = taskDescription
            },
            RequirementsSpec = requirementsSpec,
            Requirements = new AgentResult
            {
                AgentName = "RequirementsAgent",
                Summary = requirementsSummary,
                RequirementsSpec = requirementsSpec
            },
            ArchitecturePlan = architecturePlan,
            Architecture = new AgentResult
            {
                AgentName = "ArchitectureAgent",
                Summary = architectureSummary,
                ArchitecturePlan = architecturePlan
            }
        };
    }

    private static string ResolvePath(string repoPath, string? checkpointPath) =>
        string.IsNullOrWhiteSpace(checkpointPath)
            ? DefaultCheckpointPath(repoPath)
            : Path.GetFullPath(checkpointPath);

    private static WorkflowStateCheckpointData ToData(WorkflowState state) =>
        new()
        {
            Version = CurrentVersion,
            TaskTitle = state.Task.Title,
            TaskDescription = state.Task.Description,
            TaskHash = ComputeTaskHash(state.Task.Description),
            RepoPath = state.RepoPath,
            Stage = state.Stage,
            RecoveryAttemptCount = state.RecoveryAttemptCount,
            ProjectStructureContext = state.ProjectStructureContext,
            LegacyImplementationContext = state.LegacyImplementationContext,
            CombinedRagContext = state.CombinedRagContext,
            Requirements = ToAgentData(state.Requirements),
            RequirementsSpec = state.RequirementsSpec,
            Architecture = ToAgentData(state.Architecture),
            ArchitecturePlan = state.ArchitecturePlan,
            AcceptanceCriteria = ToAgentData(state.AcceptanceCriteria),
            Observer = ToAgentData(state.Observer),
            Backend = ToAgentData(state.Backend),
            Frontend = ToAgentData(state.Frontend),
            BuildValidation = ToAgentData(state.BuildValidation),
            Audit = ToAgentData(state.Audit),
            Recovery = ToAgentData(state.Recovery),
            CompilationFixAllowedFiles = state.CompilationFixAllowedFiles.ToList(),
            CompilationFixExemplarContext = state.CompilationFixExemplarContext,
            ComplianceIssues = state.ComplianceIssues.ToList(),
            DeferredTestEntities = state.DeferredTestEntities.ToList(),
            PullRequestUrl = state.PullRequestUrl,
            PullRequestStatus = state.PullRequestStatus,
            AppliedFiles = state.AppliedFiles.ToList(),
            Timeline = state.Timeline.ToList()
        };

    private static WorkflowState FromData(WorkflowStateCheckpointData data, string repoPath)
    {
        var state = new WorkflowState
        {
            RepoPath = repoPath,
            Stage = data.Stage,
            RecoveryAttemptCount = data.RecoveryAttemptCount,
            ProjectStructureContext = data.ProjectStructureContext,
            LegacyImplementationContext = data.LegacyImplementationContext,
            CombinedRagContext = data.CombinedRagContext,
            Task = new WorkflowTask
            {
                Title = string.IsNullOrWhiteSpace(data.TaskTitle) ? "Resumed Development Task" : data.TaskTitle,
                Description = data.TaskDescription
            },
            Requirements = FromAgentData(data.Requirements),
            RequirementsSpec = data.RequirementsSpec ?? data.Requirements?.RequirementsSpec,
            Architecture = FromAgentData(data.Architecture),
            ArchitecturePlan = data.ArchitecturePlan ?? data.Architecture?.ArchitecturePlan,
            AcceptanceCriteria = FromAgentData(data.AcceptanceCriteria),
            Observer = FromAgentData(data.Observer),
            Backend = FromAgentData(data.Backend),
            Frontend = FromAgentData(data.Frontend),
            BuildValidation = FromAgentData(data.BuildValidation),
            Audit = FromAgentData(data.Audit),
            Recovery = FromAgentData(data.Recovery),
            CompilationFixExemplarContext = data.CompilationFixExemplarContext,
            PullRequestUrl = data.PullRequestUrl,
            PullRequestStatus = data.PullRequestStatus
        };

        state.CompilationFixAllowedFiles.AddRange(data.CompilationFixAllowedFiles);
        state.ComplianceIssues.AddRange(data.ComplianceIssues);
        foreach (string entity in data.DeferredTestEntities)
        {
            state.DeferredTestEntities.Add(entity);
        }

        state.AppliedFiles.AddRange(data.AppliedFiles);
        foreach (string line in data.Timeline)
        {
            state.Timeline.Add(line);
        }

        return state;
    }

    private static AgentResultData? ToAgentData(AgentResult? result)
    {
        if (result is null)
        {
            return null;
        }

        return new AgentResultData
        {
            AgentName = result.AgentName,
            Summary = result.Summary,
            ArchitecturePlan = result.ArchitecturePlan,
            RequirementsSpec = result.RequirementsSpec,
            AcceptanceCriteriaReport = result.AcceptanceCriteriaReport,
            ProposedFiles = result.ProposedFiles
                .Select(file => new GeneratedFileData { RelativePath = file.RelativePath, Content = file.Content })
                .ToList(),
            Findings = result.Findings
                .Select(finding => new AgentFindingData { Severity = finding.Severity, Message = finding.Message })
                .ToList(),
            ProductionBuildPassed = result.ProductionBuildPassed,
            TestsPassed = result.TestsPassed
        };
    }

    private static AgentResult? FromAgentData(AgentResultData? data)
    {
        if (data is null)
        {
            return null;
        }

        return new AgentResult
        {
            AgentName = data.AgentName,
            Summary = data.Summary,
            ArchitecturePlan = data.ArchitecturePlan,
            RequirementsSpec = data.RequirementsSpec,
            AcceptanceCriteriaReport = data.AcceptanceCriteriaReport,
            ProposedFiles = data.ProposedFiles
                .Select(file => new GeneratedFile { RelativePath = file.RelativePath, Content = file.Content })
                .ToList(),
            Findings = data.Findings
                .Select(finding => new AgentFinding { Severity = finding.Severity, Message = finding.Message })
                .ToList(),
            ProductionBuildPassed = data.ProductionBuildPassed,
            TestsPassed = data.TestsPassed
        };
    }
}
