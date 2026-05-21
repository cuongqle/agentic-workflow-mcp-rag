using System.Text.Json;

namespace agents_mcp_rag.Configuration;

public static class AppSettingsLoader
{
    public static AppSettings Load(string? configPath = null)
    {
        string resolvedPath = configPath ?? FindAppSettingsPath()
            ?? throw new FileNotFoundException("Missing appsettings.json.");

        string json = File.ReadAllText(resolvedPath);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        string openAIKey = GetRequiredString(root, "OpenAI", "ApiKey");
        string openAIModel = GetOptionalString(root, "OpenAI", "ChatModel", "gpt-4o");
        string openAIEmbeddingModel = GetOptionalString(root, "OpenAI", "EmbeddingModel", "text-embedding-3-small");
        string githubPat = GetRequiredString(root, "GitHub", "Pat");
        string repoPath = GetRequiredString(root, "Repo", "Path");
        int maxRecoveryAttempts = 2;
        int maxCompilationFixAttempts = 3;
        int compilationFixMaxContextChars = 200_000;
        int compilationFixMaxOptionalFiles = 0;
        bool useHybridRag = true;
        double ragLexicalWeight = 0.55;
        double ragVectorWeight = 0.45;
        bool autoCreatePullRequest = true;
        string pullRequestBaseBranch = "main";
        string defaultTaskPrompt = "Implement a new feature safely with architecture-first planning and audited delivery.";

        if (root.TryGetProperty("Workflow", out var workflowNode))
        {
            if (workflowNode.TryGetProperty("MaxRecoveryAttempts", out var maxRecoveryNode)
                && maxRecoveryNode.TryGetInt32(out int parsedMaxRecovery))
            {
                maxRecoveryAttempts = parsedMaxRecovery;
            }

            if (workflowNode.TryGetProperty("MaxCompilationFixAttempts", out var maxCompilationFixNode)
                && maxCompilationFixNode.TryGetInt32(out int parsedMaxCompilationFix))
            {
                maxCompilationFixAttempts = parsedMaxCompilationFix;
            }

            if (workflowNode.TryGetProperty("CompilationFixMaxContextChars", out var maxContextCharsNode)
                && maxContextCharsNode.TryGetInt32(out int parsedMaxContextChars))
            {
                compilationFixMaxContextChars = parsedMaxContextChars;
            }

            if (workflowNode.TryGetProperty("CompilationFixMaxOptionalFiles", out var maxOptionalFilesNode)
                && maxOptionalFilesNode.TryGetInt32(out int parsedMaxOptionalFiles))
            {
                compilationFixMaxOptionalFiles = parsedMaxOptionalFiles;
            }

            if (workflowNode.TryGetProperty("UseHybridRag", out var useHybridNode)
                && useHybridNode.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                useHybridRag = useHybridNode.GetBoolean();
            }

            if (workflowNode.TryGetProperty("RagLexicalWeight", out var lexicalWeightNode)
                && lexicalWeightNode.TryGetDouble(out var parsedLexicalWeight))
            {
                ragLexicalWeight = parsedLexicalWeight;
            }

            if (workflowNode.TryGetProperty("RagVectorWeight", out var vectorWeightNode)
                && vectorWeightNode.TryGetDouble(out var parsedVectorWeight))
            {
                ragVectorWeight = parsedVectorWeight;
            }

            if (workflowNode.TryGetProperty("AutoCreatePullRequest", out var autoPrNode)
                && autoPrNode.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                autoCreatePullRequest = autoPrNode.GetBoolean();
            }

            if (workflowNode.TryGetProperty("PullRequestBaseBranch", out var prBaseBranchNode)
                && !string.IsNullOrWhiteSpace(prBaseBranchNode.GetString()))
            {
                pullRequestBaseBranch = prBaseBranchNode.GetString()!;
            }

            if (workflowNode.TryGetProperty("DefaultTaskPrompt", out var defaultPromptNode)
                && !string.IsNullOrWhiteSpace(defaultPromptNode.GetString()))
            {
                defaultTaskPrompt = defaultPromptNode.GetString()!;
            }
        }

        if (!IsRemoteRepository(repoPath) && !Path.IsPathRooted(repoPath))
        {
            repoPath = Path.GetFullPath(repoPath, Path.GetDirectoryName(resolvedPath)!);
        }

        return new AppSettings
        {
            OpenAIKey = openAIKey,
            OpenAIModel = openAIModel,
            OpenAIEmbeddingModel = openAIEmbeddingModel,
            GitHubPat = githubPat,
            RepoPath = repoPath,
            MaxRecoveryAttempts = maxRecoveryAttempts,
            MaxCompilationFixAttempts = maxCompilationFixAttempts,
            UseHybridRag = useHybridRag,
            RagLexicalWeight = ragLexicalWeight,
            RagVectorWeight = ragVectorWeight,
            DefaultTaskPrompt = defaultTaskPrompt,
            AutoCreatePullRequest = autoCreatePullRequest,
            PullRequestBaseBranch = pullRequestBaseBranch,
            CompilationFixContext = new CompilationFixContextOptions
            {
                MaxTotalChars = compilationFixMaxContextChars,
                MaxOptionalFiles = compilationFixMaxOptionalFiles
            }
        };
    }

    private static string? FindAppSettingsPath()
    {
        string? fromCurrent = FindFileInParents(Directory.GetCurrentDirectory(), "appsettings.json");
        if (fromCurrent is not null)
        {
            return fromCurrent;
        }

        return FindFileInParents(AppContext.BaseDirectory, "appsettings.json");
    }

    private static string? FindFileInParents(string startDirectory, string fileName)
    {
        DirectoryInfo? current = new DirectoryInfo(startDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }

    private static string GetRequiredString(JsonElement root, string section, string key)
    {
        string value = root.GetProperty(section).GetProperty(key).GetString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentNullException($"Missing {section}:{key} in appsettings.json.");
        }

        return value;
    }

    private static string GetOptionalString(JsonElement root, string section, string key, string fallback)
    {
        if (root.TryGetProperty(section, out var sectionNode)
            && sectionNode.TryGetProperty(key, out var valueNode)
            && !string.IsNullOrWhiteSpace(valueNode.GetString()))
        {
            return valueNode.GetString()!;
        }

        return fallback;
    }

    private static bool IsRemoteRepository(string path)
    {
        return path.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("git@", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase);
    }
}
