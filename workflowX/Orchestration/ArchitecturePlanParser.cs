using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

/// <summary>
/// Parses architecture agent output — JSON first, markdown/text fallback.
/// </summary>
static class ArchitecturePlanParser
{
    public static bool TryParseJson(string raw, out ArchitecturePlan? plan, out string summary)
    {
        plan = null;
        summary = raw;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        string candidate = ExtractJsonCandidate(raw);
        try
        {
            using var document = JsonDocument.Parse(candidate);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            summary = root.TryGetProperty("summary", out JsonElement summaryNode) && summaryNode.ValueKind == JsonValueKind.String
                ? summaryNode.GetString() ?? string.Empty
                : string.Empty;

            string testStrategy = ReadStringProperty(root, "testStrategy");
            string rollbackNotes = ReadStringProperty(root, "rollbackNotes");

            IReadOnlyList<ArchitectureDeliverable> backendFiles = ReadDeliverables(root, "backendFiles");
            IReadOnlyList<ArchitectureDeliverable> frontendFiles = ReadDeliverables(root, "frontendFiles");

            plan = new ArchitecturePlan
            {
                Rationale = summary,
                BackendFiles = backendFiles,
                FrontendFiles = frontendFiles,
                TestStrategy = testStrategy,
                RollbackNotes = rollbackNotes
            };

            summary = FormatReadableSummary(plan);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static ArchitecturePlan? ParseMarkdown(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        IReadOnlyList<ArchitectureDeliverable> backendFiles = ReadDeliverablesFromText(text, WorkflowFindingRules.ExtractBackendPaths(text));
        IReadOnlyList<ArchitectureDeliverable> frontendFiles = ReadDeliverablesFromText(text, WorkflowFindingRules.ExtractFrontendPaths(text));

        if (backendFiles.Count == 0 && frontendFiles.Count == 0
            && !WorkflowFindingRules.HasArchitectureSection(text, "BACKEND_FILES")
            && !WorkflowFindingRules.HasArchitectureSection(text, "FRONTEND_FILES"))
        {
            return null;
        }

        return new ArchitecturePlan
        {
            Rationale = text,
            BackendFiles = backendFiles,
            FrontendFiles = frontendFiles
        };
    }

    public static ArchitecturePlan Resolve(AgentResult? architectureResult)
    {
        if (architectureResult?.ArchitecturePlan is not null)
        {
            return architectureResult.ArchitecturePlan;
        }

        if (architectureResult is not null
            && TryParseJson(architectureResult.Summary, out ArchitecturePlan? fromSummary, out _)
            && fromSummary is not null)
        {
            return fromSummary;
        }

        return ParseMarkdown(architectureResult?.Summary)
               ?? new ArchitecturePlan { Rationale = architectureResult?.Summary ?? string.Empty };
    }

    public static string FormatReadableSummary(ArchitecturePlan plan)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(plan.Rationale))
        {
            sb.AppendLine(plan.Rationale.Trim());
            sb.AppendLine();
        }

        AppendDeliverableSection(sb, "BACKEND_FILES", plan.BackendFiles);
        AppendDeliverableSection(sb, "FRONTEND_FILES", plan.FrontendFiles);

        if (!string.IsNullOrWhiteSpace(plan.TestStrategy))
        {
            sb.AppendLine();
            sb.AppendLine("Test strategy:");
            sb.AppendLine(plan.TestStrategy.Trim());
        }

        if (!string.IsNullOrWhiteSpace(plan.RollbackNotes))
        {
            sb.AppendLine();
            sb.AppendLine("Rollback notes:");
            sb.AppendLine(plan.RollbackNotes.Trim());
        }

        return sb.ToString().Trim();
    }

    private static void AppendDeliverableSection(StringBuilder sb, string title, IReadOnlyList<ArchitectureDeliverable> files)
    {
        if (files.Count == 0)
        {
            return;
        }

        sb.AppendLine($"{title}:");
        foreach (ArchitectureDeliverable file in files)
        {
            sb.Append("- ");
            sb.Append(file.Path);
            if (!string.IsNullOrWhiteSpace(file.Description))
            {
                sb.Append(": ");
                sb.Append(file.Description.Trim());
            }

            sb.AppendLine();
        }
    }

    private static IReadOnlyList<ArchitectureDeliverable> ReadDeliverables(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement array) || array.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ArchitectureDeliverable>();
        }

        var deliverables = new List<ArchitectureDeliverable>();
        foreach (JsonElement item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string path = item.TryGetProperty("path", out JsonElement pathNode) ? pathNode.GetString() ?? string.Empty : string.Empty;
            path = path.Trim().Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            string description = item.TryGetProperty("description", out JsonElement descriptionNode)
                ? descriptionNode.GetString() ?? string.Empty
                : string.Empty;
            deliverables.Add(new ArchitectureDeliverable(path, description));
        }

        return deliverables;
    }

    private static IReadOnlyList<ArchitectureDeliverable> ReadDeliverablesFromText(string text, IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return Array.Empty<ArchitectureDeliverable>();
        }

        return paths
            .Select(path => new ArchitectureDeliverable(path, ExtractDescriptionForPath(text, path)))
            .ToList();
    }

    private static string ExtractDescriptionForPath(string text, string path)
    {
        string normalizedPath = path.Replace('\\', '/');
        string escaped = Regex.Escape(normalizedPath);
        var match = Regex.Match(
            text,
            $@"(?:^\s*(?:-\s*|\d+\.\s*))[`""']?{escaped}[`""']?\s*:\s*(.+)$",
            RegexOptions.IgnoreCase | RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
    }

    private static string ReadStringProperty(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out JsonElement node) && node.ValueKind == JsonValueKind.String
            ? node.GetString() ?? string.Empty
            : string.Empty;

    private static string ExtractJsonCandidate(string raw)
    {
        var fenced = Regex.Match(raw, "```(?:json)?\\s*(\\{[\\s\\S]*\\})\\s*```", RegexOptions.IgnoreCase);
        if (fenced.Success && fenced.Groups.Count > 1)
        {
            return fenced.Groups[1].Value;
        }

        int firstBrace = raw.IndexOf('{');
        int lastBrace = raw.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            return raw.Substring(firstBrace, lastBrace - firstBrace + 1);
        }

        return raw;
    }
}
