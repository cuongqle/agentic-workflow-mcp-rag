using System.Text;

static class WorkflowArtifactWriter
{
    public static Task<string> WriteAsync(WorkflowState state)
    {
        string outputDir = Path.Combine(state.RepoPath, "agents-mcp-rag-output");
        Directory.CreateDirectory(outputDir);

        WriteAgentFile(outputDir, "architecture-plan.md", state.Architecture);
        WriteAgentFile(outputDir, "backend-plan.md", state.Backend);
        WriteAgentFile(outputDir, "frontend-plan.md", state.Frontend);
        WriteAgentFile(outputDir, "build-validation-report.md", state.BuildValidation);
        WriteAgentFile(outputDir, "observer-report.md", state.Observer);
        WriteAgentFile(outputDir, "audit-report.md", state.Audit);
        WriteAgentFile(outputDir, "recovery-plan.md", state.Recovery);
        WriteTimeline(outputDir, state);

        return Task.FromResult(outputDir);
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
