using System.Text.RegularExpressions;

namespace workflowX.Infrastructure;

/// <summary>
/// Derives human-readable task titles and git-safe branch slugs from prompts and requirements.
/// </summary>
public static class WorkflowTaskNaming
{
    private const int MaxTitleLength = 80;
    private const int MaxBranchSlugLength = 32;
    private const int MaxBranchSlugWords = 3;

    private static readonly HashSet<string> BranchStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "to", "be", "is", "are", "was", "were", "in", "on", "at", "by", "from",
        "and", "or", "but", "so", "that", "this", "these", "those", "it", "its", "as", "of", "for",
        "with", "into", "onto", "upon", "over", "under", "between", "through", "during", "before",
        "after", "above", "below", "out", "up", "down", "off", "about", "against", "within",
        "feature", "features", "implement", "implemented", "implementation", "implementing",
        "add", "added", "create", "created", "build", "built", "introduce", "introduced",
        "new", "existing", "using", "use", "used", "via", "based", "safe", "safely",
        "have", "has", "had", "do", "does", "did", "will", "would", "should", "could", "can",
        "may", "might", "must", "need", "needed", "being", "been", "also", "just", "only",
        "to", "codebase", "repository", "repo", "application", "app", "system",
        "user", "users", "want", "wants", "manager", "managers"
    };

    public static string InferTitle(string taskPrompt)
    {
        if (string.IsNullOrWhiteSpace(taskPrompt))
        {
            return "Development Task";
        }

        string trimmed = taskPrompt.Trim();
        string? entityName = InferTargetEntityName(trimmed);
        if (!string.IsNullOrWhiteSpace(entityName))
        {
            return BuildEntityTitle(trimmed, entityName);
        }

        string firstLine = trimmed
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()
            ?.Trim() ?? trimmed;

        return TruncateTitle(NormalizeTitleLine(firstLine));
    }

    public static string InferTitleFromUserStory(string userStory)
    {
        if (string.IsNullOrWhiteSpace(userStory))
        {
            return string.Empty;
        }

        var wantClause = Regex.Match(
            userStory.Trim(),
            @"As\s+an?\s+.+?,\s*I\s+want\s+(.+?)(?:,\s*so\s+that\b|$)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        if (wantClause.Success && wantClause.Groups.Count > 1)
        {
            return TruncateTitle(NormalizeTitleLine(StripLeadingTo(wantClause.Groups[1].Value)));
        }

        string firstLine = userStory
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()
            ?.Trim() ?? userStory.Trim();

        return TruncateTitle(NormalizeTitleLine(firstLine));
    }

    public static void ApplyPromptToTask(WorkflowState state, string taskPrompt)
    {
        if (string.IsNullOrWhiteSpace(taskPrompt))
        {
            return;
        }

        string title = InferTitle(taskPrompt);
        state.Task = new WorkflowTask
        {
            Title = title,
            Description = taskPrompt
        };
    }

    public static void RefineTaskFromRequirements(WorkflowState state)
    {
        string? userStory = state.RequirementsSpec?.UserStory;
        if (string.IsNullOrWhiteSpace(userStory))
        {
            return;
        }

        string refinedTitle = InferTitleFromUserStory(userStory);
        if (string.IsNullOrWhiteSpace(refinedTitle))
        {
            return;
        }

        state.Task = new WorkflowTask
        {
            Title = refinedTitle,
            Description = state.Task.Description
        };
    }

    public static string ResolveBranchSlug(WorkflowState state)
    {
        string? userStory = state.RequirementsSpec?.UserStory;
        if (!string.IsNullOrWhiteSpace(userStory))
        {
            string? slug = ResolveBranchSlugFromText(userStory);
            if (!string.IsNullOrWhiteSpace(slug))
            {
                return slug;
            }
        }

        string? fromDescription = InferBranchSubject(state.Task.Description);
        if (!string.IsNullOrWhiteSpace(fromDescription))
        {
            return ToBranchSlug(fromDescription);
        }

        if (!string.IsNullOrWhiteSpace(state.Task.Title))
        {
            string? fromTitle = InferBranchSubject(state.Task.Title);
            if (!string.IsNullOrWhiteSpace(fromTitle))
            {
                return ToBranchSlug(fromTitle);
            }

            string titleSlug = ToBranchSlug(state.Task.Title);
            if (!string.IsNullOrWhiteSpace(titleSlug))
            {
                return titleSlug;
            }
        }

        return ToBranchSlug(InferTitle(state.Task.Description));
    }

    private static string? ResolveBranchSlugFromText(string text)
    {
        if (Regex.IsMatch(text, @"\bI\s+want\b", RegexOptions.IgnoreCase))
        {
            string wantClause = InferTitleFromUserStory(text);
            string wantSlug = ToBranchSlug(wantClause);
            if (!string.IsNullOrWhiteSpace(wantSlug))
            {
                return wantSlug;
            }
        }

        string? fromSubject = InferBranchSubject(text);
        if (!string.IsNullOrWhiteSpace(fromSubject))
        {
            return ToBranchSlug(fromSubject);
        }

        return null;
    }

    /// <summary>
    /// Extracts a short subject for branch names (entity name or compact noun phrase).
    /// </summary>
    public static string? InferBranchSubject(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string? entity = InferTargetEntityName(text) ?? InferSubjectNoun(text);
        if (!string.IsNullOrWhiteSpace(entity))
        {
            return entity;
        }

        string compact = BuildCompactSlugPhrase(text);
        return string.IsNullOrWhiteSpace(compact) ? null : compact.Replace('-', ' ');
    }

    public static string FormatFeatureBranchName(string slug)
    {
        string normalized = string.IsNullOrWhiteSpace(slug) ? "development-task" : slug;
        return $"agents/{normalized}";
    }

    public static string ResolveFeatureBranchName(WorkflowState state) =>
        FormatFeatureBranchName(ResolveBranchSlug(state));

    public static string? InferTargetEntityName(string taskPrompt)
    {
        foreach (Match match in Regex.Matches(taskPrompt, @"'([A-Za-z][A-Za-z0-9_]*)'"))
        {
            if (match.Groups.Count > 1)
            {
                return match.Groups[1].Value;
            }
        }

        Match entityPattern = Regex.Match(
            taskPrompt,
            @"entity\s+called\s+([A-Za-z][A-Za-z0-9_]*)",
            RegexOptions.IgnoreCase);
        if (entityPattern.Success && entityPattern.Groups.Count > 1)
        {
            return entityPattern.Groups[1].Value;
        }

        Match namedEntity = Regex.Match(
            taskPrompt,
            @"\b(?:entity|model)\s+([A-Za-z][A-Za-z0-9_]*)\b",
            RegexOptions.IgnoreCase);
        if (namedEntity.Success && namedEntity.Groups.Count > 1)
        {
            return namedEntity.Groups[1].Value;
        }

        Match namedFeature = Regex.Match(
            taskPrompt,
            @"\bfeatures?\s+(?:called|named)\s+['""]?([A-Za-z][A-Za-z0-9_]*)['""]?",
            RegexOptions.IgnoreCase);
        if (namedFeature.Success && namedFeature.Groups.Count > 1)
        {
            return namedFeature.Groups[1].Value;
        }

        return null;
    }

    public static string? InferSubjectNoun(string text)
    {
        string[] patterns =
        [
            @"(?i)\b(?:a|an|the|new)\s+([a-z][a-z0-9_-]+)\s+features?\b",
            @"(?i)\bfeatures?\s+(?:for|of|called|named)\s+['""]?([a-z][a-z0-9_-]+)",
            @"(?i)\b(?:implement|add|create|build|introduce)\s+(?:a|an|the|new)?\s*([a-z][a-z0-9_-]+)(?:\s+features?)?\b",
            @"(?i)\b(?:implement|add|create|build)\s+['""]?([A-Za-z][A-Za-z0-9_]*)['""]?"
        ];

        foreach (string pattern in patterns)
        {
            Match match = Regex.Match(text, pattern);
            if (match.Success && match.Groups.Count > 1)
            {
                string candidate = match.Groups[1].Value;
                if (IsSignificantBranchToken(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    public static string ToBranchSlug(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string compact = BuildCompactSlugPhrase(value);
        if (string.IsNullOrWhiteSpace(compact))
        {
            compact = SanitizeSlug(value);
        }

        if (string.IsNullOrWhiteSpace(compact))
        {
            return string.Empty;
        }

        if (compact.Length <= MaxBranchSlugLength)
        {
            return compact;
        }

        string truncated = compact[..MaxBranchSlugLength].TrimEnd('-');
        int lastSeparator = truncated.LastIndexOf('-');
        return lastSeparator > 4 ? truncated[..lastSeparator] : truncated;
    }

    private static string BuildCompactSlugPhrase(string value)
    {
        IEnumerable<string> words = Regex.Split(value, @"[^A-Za-z0-9]+")
            .Select(word => word.Trim())
            .Where(IsSignificantBranchToken)
            .Take(MaxBranchSlugWords);

        return SanitizeSlug(string.Join("-", words));
    }

    private static bool IsSignificantBranchToken(string word) =>
        word.Length >= 3 && !BranchStopWords.Contains(word);

    private static string SanitizeSlug(string value)
    {
        var chars = value
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        string sanitized = new string(chars);
        return string.Join('-', sanitized.Split('-', StringSplitOptions.RemoveEmptyEntries)).Trim('-');
    }

    private static string BuildEntityTitle(string taskPrompt, string entityName)
    {
        if (Regex.IsMatch(taskPrompt, @"\b(?:implement|add|create|build|introduce)\b", RegexOptions.IgnoreCase))
        {
            return TruncateTitle($"{NormalizeAction(taskPrompt)} {entityName}");
        }

        return TruncateTitle(entityName);
    }

    private static string NormalizeAction(string taskPrompt)
    {
        if (Regex.IsMatch(taskPrompt, @"\bimplement\b", RegexOptions.IgnoreCase))
        {
            return "Implement";
        }

        if (Regex.IsMatch(taskPrompt, @"\badd\b", RegexOptions.IgnoreCase))
        {
            return "Add";
        }

        if (Regex.IsMatch(taskPrompt, @"\bcreate\b", RegexOptions.IgnoreCase))
        {
            return "Create";
        }

        if (Regex.IsMatch(taskPrompt, @"\bbuild\b", RegexOptions.IgnoreCase))
        {
            return "Build";
        }

        return "Implement";
    }

    private static string StripLeadingTo(string value)
    {
        string trimmed = value.Trim();
        if (trimmed.StartsWith("to ", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed[3..].TrimStart();
        }

        return trimmed;
    }

    private static string NormalizeTitleLine(string line)
    {
        string normalized = Regex.Replace(line.Trim(), @"\s+", " ");
        if (normalized.Length == 0)
        {
            return normalized;
        }

        return char.ToUpperInvariant(normalized[0]) + normalized[1..];
    }

    private static string TruncateTitle(string title)
    {
        if (title.Length <= MaxTitleLength)
        {
            return title;
        }

        int cut = title.LastIndexOf(' ', MaxTitleLength);
        if (cut < 20)
        {
            cut = MaxTitleLength;
        }

        return title[..cut].TrimEnd('.', ',', ';', ':') + "...";
    }
}
