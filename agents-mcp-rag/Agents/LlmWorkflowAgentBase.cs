using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

abstract class LlmWorkflowAgentBase : IWorkflowAgent
{
    private readonly Kernel _kernel;

    protected LlmWorkflowAgentBase(Kernel kernel)
    {
        _kernel = kernel;
    }

    public abstract string Name { get; }

    protected Kernel Kernel => _kernel;

    protected abstract string BuildPrompt(WorkflowState state);
    protected virtual IReadOnlyList<AgentFinding> BuildFallbackFindings() => Array.Empty<AgentFinding>();

    public virtual async Task<AgentResult> ExecuteAsync(WorkflowState state, CancellationToken cancellationToken = default)
    {
        string summary;
        List<GeneratedFile> generatedFiles = new();
        try
        {
            string prompt = BuildPrompt(state);
            var chat = _kernel.GetRequiredService<IChatCompletionService>();
            var history = new ChatHistory();
            history.AddUserMessage(prompt);
            var response = await chat.GetChatMessageContentsAsync(history, cancellationToken: cancellationToken);
            string raw = response.FirstOrDefault()?.Content ?? string.Empty;
            if (TryParseStructuredResponse(raw, out string parsedSummary, out List<GeneratedFile> parsedFiles))
            {
                summary = parsedSummary;
                generatedFiles = parsedFiles;
            }
            else
            {
                summary = raw;
            }
        }
        catch (Exception ex)
        {
            summary = $"Fallback output because LLM call failed in {Name}: {ex.Message}";
        }

        return new AgentResult
        {
            AgentName = Name,
            Summary = summary,
            ProposedFiles = generatedFiles,
            Findings = new List<AgentFinding>(BuildFallbackFindings())
        };
    }

    private static bool TryParseStructuredResponse(string raw, out string summary, out List<GeneratedFile> files)
    {
        summary = raw;
        files = new();
        string candidate = ExtractJsonCandidate(raw);
        try
        {
            using var document = JsonDocument.Parse(candidate);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (root.TryGetProperty("summary", out var summaryNode) && summaryNode.ValueKind == JsonValueKind.String)
            {
                summary = summaryNode.GetString() ?? raw;
            }

            if (root.TryGetProperty("files", out var filesNode) && filesNode.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in filesNode.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    string path = item.TryGetProperty("path", out var pathNode) ? pathNode.GetString() ?? string.Empty : string.Empty;
                    string content = item.TryGetProperty("content", out var contentNode) ? contentNode.GetString() ?? string.Empty : string.Empty;
                    if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(content))
                    {
                        continue;
                    }

                    files.Add(new GeneratedFile
                    {
                        RelativePath = path.Trim(),
                        Content = content
                    });
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string ExtractJsonCandidate(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return raw;
        }

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
