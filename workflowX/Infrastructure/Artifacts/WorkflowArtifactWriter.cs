using System.Text;
using System.Text.Json;

static class WorkflowArtifactWriter
{
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string OutputDirectory(WorkflowState state) =>
        Path.Combine(state.RepoPath, "workflowX-output");

    public static string WriteRequirementsArtifacts(WorkflowState state)
    {
        string outputDir = OutputDirectory(state);
        Directory.CreateDirectory(outputDir);

        WriteRequirementsMarkdown(outputDir, state);
        WriteRequirementsJson(outputDir, state);
        WriteAgentFile(outputDir, "requirements-agent.md", state.Requirements);

        return outputDir;
    }

    public static string WriteAcceptanceArtifacts(WorkflowState state)
    {
        string outputDir = OutputDirectory(state);
        Directory.CreateDirectory(outputDir);

        WriteAcceptanceMarkdown(outputDir, state);
        WriteAcceptanceJson(outputDir, state);
        WriteAgentFile(outputDir, "acceptance-criteria-agent.md", state.AcceptanceCriteria);

        return outputDir;
    }

    /// <summary>
    /// Writes architecture artifacts as soon as planning completes so they are visible even if the workflow blocks later.
    /// </summary>
    public static string WriteArchitectureArtifacts(WorkflowState state)
    {
        string outputDir = OutputDirectory(state);
        Directory.CreateDirectory(outputDir);

        WriteArchitecturePlanMarkdown(outputDir, state);
        WriteArchitecturePlanJson(outputDir, state);
        WriteAgentFile(outputDir, "architecture-agent.md", state.Architecture);

        return outputDir;
    }

    public static Task<string> WriteAsync(WorkflowState state)
    {
        string outputDir = OutputDirectory(state);
        Directory.CreateDirectory(outputDir);

        WriteArchitecturePlanMarkdown(outputDir, state);
        WriteArchitecturePlanJson(outputDir, state);
        WriteAgentFile(outputDir, "architecture-agent.md", state.Architecture);
        WriteRequirementsMarkdown(outputDir, state);
        WriteRequirementsJson(outputDir, state);
        WriteAgentFile(outputDir, "requirements-agent.md", state.Requirements);
        WriteAcceptanceMarkdown(outputDir, state);
        WriteAcceptanceJson(outputDir, state);
        WriteAgentFile(outputDir, "acceptance-criteria-agent.md", state.AcceptanceCriteria);
        WriteAgentFile(outputDir, "backend-plan.md", state.Backend);
        WriteAgentFile(outputDir, "frontend-plan.md", state.Frontend);
        WriteAgentFile(outputDir, "build-validation-report.md", state.BuildValidation);
        WriteAgentFile(outputDir, "observer-report.md", state.Observer);
        WriteAgentFile(outputDir, "audit-report.md", state.Audit);
        WriteAgentFile(outputDir, "recovery-plan.md", state.Recovery);
        WriteTimeline(outputDir, state);

        return Task.FromResult(outputDir);
    }

    static void WriteArchitecturePlanMarkdown(string outputDir, WorkflowState state)
    {
        var content = new StringBuilder();
        content.AppendLine("# Architecture Plan");
        content.AppendLine();
        content.AppendLine($"- Task: {state.Task.Title}");
        content.AppendLine($"- Repo: {state.RepoPath}");
        content.AppendLine();

        if (state.ArchitecturePlan is not null)
        {
            content.AppendLine(ArchitecturePlanParser.FormatReadableSummary(state.ArchitecturePlan));
        }
        else if (!string.IsNullOrWhiteSpace(state.Architecture?.Summary))
        {
            content.AppendLine(state.Architecture.Summary);
        }
        else
        {
            content.AppendLine("_No architecture plan available._");
        }

        File.WriteAllText(Path.Combine(outputDir, "architecture-plan.md"), content.ToString());
    }

    static void WriteArchitecturePlanJson(string outputDir, WorkflowState state)
    {
        if (state.ArchitecturePlan is null)
        {
            return;
        }

        var payload = new
        {
            summary = state.ArchitecturePlan.Rationale,
            backendFiles = state.ArchitecturePlan.BackendFiles.Select(file => new { path = file.Path, description = file.Description }),
            frontendFiles = state.ArchitecturePlan.FrontendFiles.Select(file => new { path = file.Path, description = file.Description }),
            testStrategy = state.ArchitecturePlan.TestStrategy,
            rollbackNotes = state.ArchitecturePlan.RollbackNotes
        };

        string json = JsonSerializer.Serialize(payload, JsonOptions);
        File.WriteAllText(Path.Combine(outputDir, "architecture-plan.json"), json);
    }

    static void WriteRequirementsMarkdown(string outputDir, WorkflowState state)
    {
        var content = new StringBuilder();
        content.AppendLine("# Requirements");
        content.AppendLine();
        content.AppendLine($"- Task: {state.Task.Title}");
        content.AppendLine();

        if (state.RequirementsSpec is not null)
        {
            content.AppendLine(RequirementsSpecParser.FormatReadableSummary(state.RequirementsSpec));
        }
        else if (!string.IsNullOrWhiteSpace(state.Requirements?.Summary))
        {
            content.AppendLine(state.Requirements.Summary);
        }
        else
        {
            content.AppendLine("_No requirements available._");
        }

        File.WriteAllText(Path.Combine(outputDir, "requirements.md"), content.ToString());
    }

    static void WriteRequirementsJson(string outputDir, WorkflowState state)
    {
        if (state.RequirementsSpec is null)
        {
            return;
        }

        var payload = new
        {
            userStory = state.RequirementsSpec.UserStory,
            acceptanceCriteria = state.RequirementsSpec.AcceptanceCriteria.Select(criterion => new
            {
                id = criterion.Id,
                description = criterion.Description
            }),
            inScope = state.RequirementsSpec.InScope,
            outOfScope = state.RequirementsSpec.OutOfScope,
            risks = state.RequirementsSpec.Risks
        };

        string json = JsonSerializer.Serialize(payload, JsonOptions);
        File.WriteAllText(Path.Combine(outputDir, "requirements.json"), json);
    }

    static void WriteAcceptanceMarkdown(string outputDir, WorkflowState state)
    {
        var content = new StringBuilder();
        content.AppendLine("# Acceptance Criteria Gate");
        content.AppendLine();
        if (state.AcceptanceCriteria?.AcceptanceCriteriaReport is not null)
        {
            content.AppendLine(AcceptanceCriteriaReportParser.FormatReadableSummary(state.AcceptanceCriteria.AcceptanceCriteriaReport));
        }
        else if (!string.IsNullOrWhiteSpace(state.AcceptanceCriteria?.Summary))
        {
            content.AppendLine(state.AcceptanceCriteria.Summary);
        }
        else
        {
            content.AppendLine("_Acceptance criteria gate did not run._");
        }

        File.WriteAllText(Path.Combine(outputDir, "acceptance-criteria-report.md"), content.ToString());
    }

    static void WriteAcceptanceJson(string outputDir, WorkflowState state)
    {
        if (state.AcceptanceCriteria?.AcceptanceCriteriaReport is null)
        {
            return;
        }

        var payload = new
        {
            passedCount = state.AcceptanceCriteria.AcceptanceCriteriaReport.PassedCount,
            failedCount = state.AcceptanceCriteria.AcceptanceCriteriaReport.FailedCount,
            criteriaResults = state.AcceptanceCriteria.AcceptanceCriteriaReport.Evaluations.Select(evaluation => new
            {
                id = evaluation.Id,
                description = evaluation.Description,
                passed = evaluation.Passed,
                evidence = evaluation.Evidence,
                source = evaluation.Source
            })
        };

        string json = JsonSerializer.Serialize(payload, JsonOptions);
        File.WriteAllText(Path.Combine(outputDir, "acceptance-criteria-report.json"), json);
    }

    static void WriteAgentFile(string outputDir, string fileName, AgentResult? result)
    {
        if (result is null)
        {
            return;
        }

        var content = new StringBuilder();
        content.AppendLine($"# {result.AgentName}");
        content.AppendLine();
        content.AppendLine(result.Summary);
        content.AppendLine();
        if (result.Findings.Count > 0)
        {
            content.AppendLine("## Findings");
            foreach (var finding in result.Findings)
            {
                content.AppendLine($"- [{finding.Severity}] {finding.Message}");
            }
        }

        File.WriteAllText(Path.Combine(outputDir, fileName), content.ToString());
    }

    static void WriteTimeline(string outputDir, WorkflowState state)
    {
        var content = new StringBuilder();
        content.AppendLine("# Workflow Timeline");
        content.AppendLine();
        foreach (var line in state.Timeline)
        {
            content.AppendLine($"- {line}");
        }

        File.WriteAllText(Path.Combine(outputDir, "timeline.md"), content.ToString());
    }
}
