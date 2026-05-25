using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

static class RequirementsSpecParser
{
    public static bool TryParseJson(string raw, out RequirementsSpec? spec, out string summary)
    {
        spec = null;
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

            string userStory = ReadStringProperty(root, "userStory");
            IReadOnlyList<AcceptanceCriterion> criteria = ReadCriteria(root, "acceptanceCriteria");
            IReadOnlyList<string> inScope = ReadStringArray(root, "inScope");
            IReadOnlyList<string> outOfScope = ReadStringArray(root, "outOfScope");
            IReadOnlyList<string> risks = ReadStringArray(root, "risks");

            spec = new RequirementsSpec
            {
                UserStory = userStory,
                AcceptanceCriteria = criteria,
                InScope = inScope,
                OutOfScope = outOfScope,
                Risks = risks
            };

            summary = FormatReadableSummary(spec);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static RequirementsSpec Resolve(AgentResult? requirementsResult)
    {
        if (requirementsResult?.RequirementsSpec is not null)
        {
            return requirementsResult.RequirementsSpec;
        }

        if (requirementsResult is not null
            && TryParseJson(requirementsResult.Summary, out RequirementsSpec? fromSummary, out _)
            && fromSummary is not null)
        {
            return fromSummary;
        }

        return new RequirementsSpec { UserStory = requirementsResult?.Summary ?? string.Empty };
    }

    /// <summary>
    /// Uses in-memory requirements when present; otherwise loads workflowX-output/requirements.json from a prior run.
    /// </summary>
    public static RequirementsSpec ResolveForWorkflow(WorkflowState state)
    {
        if (state.RequirementsSpec?.HasAcceptanceCriteria == true)
        {
            return state.RequirementsSpec;
        }

        HydrateFromArtifactsIfMissing(state);
        return state.RequirementsSpec ?? new RequirementsSpec();
    }

    public static void HydrateFromArtifactsIfMissing(WorkflowState state)
    {
        if (state.RequirementsSpec?.HasAcceptanceCriteria == true)
        {
            return;
        }

        if (!TryLoadFromArtifacts(state.RepoPath, out RequirementsSpec? spec, out string? summary)
            || spec is null
            || !spec.HasAcceptanceCriteria)
        {
            return;
        }

        state.RequirementsSpec = spec;
        if (state.Requirements is null)
        {
            state.Requirements = new AgentResult
            {
                AgentName = "RequirementsAgent",
                Summary = summary ?? FormatReadableSummary(spec),
                RequirementsSpec = spec
            };
        }
    }

    public static bool TryLoadFromArtifacts(
        string repoPath,
        out RequirementsSpec? spec,
        out string? summary)
    {
        spec = null;
        summary = null;
        if (string.IsNullOrWhiteSpace(repoPath))
        {
            return false;
        }

        string requirementsPath = Path.Combine(
            WorkflowArtifactWriter.OutputDirectory(new WorkflowState { RepoPath = repoPath }),
            "requirements.json");
        if (!File.Exists(requirementsPath))
        {
            return false;
        }

        return TryParseJson(File.ReadAllText(requirementsPath), out spec, out summary)
               && spec is not null;
    }

    public static string FormatReadableSummary(RequirementsSpec spec)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(spec.UserStory))
        {
            sb.AppendLine("User story:");
            sb.AppendLine(spec.UserStory.Trim());
            sb.AppendLine();
        }

        AppendBulletSection(sb, "Acceptance criteria", spec.AcceptanceCriteria.Select(criterion => $"{criterion.Id}: {criterion.Description}"));
        AppendBulletSection(sb, "In scope", spec.InScope);
        AppendBulletSection(sb, "Out of scope", spec.OutOfScope);
        AppendBulletSection(sb, "Risks", spec.Risks);

        return sb.ToString().Trim();
    }

    private static void AppendBulletSection(StringBuilder sb, string title, IEnumerable<string> items)
    {
        var lines = items.Where(item => !string.IsNullOrWhiteSpace(item)).ToList();
        if (lines.Count == 0)
        {
            return;
        }

        sb.AppendLine($"{title}:");
        foreach (string line in lines)
        {
            sb.Append("- ");
            sb.AppendLine(line.Trim());
        }

        sb.AppendLine();
    }

    private static IReadOnlyList<AcceptanceCriterion> ReadCriteria(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement array) || array.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<AcceptanceCriterion>();
        }

        var criteria = new List<AcceptanceCriterion>();
        int index = 1;
        foreach (JsonElement item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                string description = item.GetString()?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(description))
                {
                    criteria.Add(new AcceptanceCriterion($"AC-{index++}", description));
                }

                continue;
            }

            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string id = item.TryGetProperty("id", out JsonElement idNode) ? idNode.GetString()?.Trim() ?? string.Empty : string.Empty;
            string descriptionFromObject = item.TryGetProperty("description", out JsonElement descriptionNode)
                ? descriptionNode.GetString()?.Trim() ?? string.Empty
                : string.Empty;
            if (string.IsNullOrWhiteSpace(descriptionFromObject))
            {
                continue;
            }

            criteria.Add(new AcceptanceCriterion(
                string.IsNullOrWhiteSpace(id) ? $"AC-{index++}" : id,
                descriptionFromObject));
        }

        return criteria;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement array) || array.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return array.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()?.Trim() ?? string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
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
