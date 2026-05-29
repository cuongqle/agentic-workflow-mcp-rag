using System.Text.Json;
using System.Text.RegularExpressions;
using workflowX.Infrastructure;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

sealed class AuditorAgent : LlmWorkflowAgentBase
{
    private const string JsonOutputSchema = """
        Return strictly valid JSON (no markdown fences):
        {
          "summary": "short audit summary",
          "findings": [
            { "severity": "blocker|high|medium|low", "message": "specific issue" }
          ]
        }
        """;

    public AuditorAgent(Kernel kernel) : base(kernel)
    {
    }

    public override string Name => "AuditorAgent";

    public override async Task<AgentResult> ExecuteAsync(WorkflowState state, CancellationToken cancellationToken = default)
    {
        string summary;
        List<AgentFinding> findings;
        try
        {
            string prompt = BuildPrompt(state);
            var chat = Kernel.GetRequiredService<IChatCompletionService>();
            var history = new ChatHistory();
            history.AddUserMessage(prompt);
            var response = await chat.GetChatMessageContentsAsync(history, cancellationToken: cancellationToken);
            string raw = response.FirstOrDefault()?.Content ?? string.Empty;

            if (TryParseAuditResponse(raw, out summary, out findings))
            {
                return new AgentResult
                {
                    AgentName = Name,
                    Summary = summary,
                    Findings = findings
                };
            }

            summary = raw;
            findings = new List<AgentFinding>(BuildFallbackFindings());
        }
        catch (Exception ex)
        {
            summary = $"Fallback output because LLM call failed in {Name}: {ex.Message}";
            findings = new List<AgentFinding>(BuildFallbackFindings());
        }

        return new AgentResult
        {
            AgentName = Name,
            Summary = summary,
            Findings = findings
        };
    }

    protected override string BuildPrompt(WorkflowState state)
    {
        string buildStatus = BuildBuildValidationRules(state);

        return $"""
            You are the auditor agent. Review release readiness (bugs, security, missing tests).

            Architecture:
            {state.Architecture?.Summary}

            Backend:
            {state.Backend?.Summary}

            Frontend:
            {state.Frontend?.Summary}

            Observer:
            {state.Observer?.Summary}

            RAG:
            {state.CombinedRagContext}

            {buildStatus}

            Rules:
            - Return findings only in JSON (see schema). Flag missing *Tests.cs for new production code as high when exemplars exist in RAG.
            - Flag wrong test paths/names as high: I-prefixed test files, class/file names that do not match the repo's existing *Tests.cs exemplar pattern, *Tests.cs under production/host .csproj folders (not under RAG test projects), or newly invented test .csproj folders not listed in RAG \"Test project references\".
            - Flag high when build output reports a type or namespace could not be found (NuGet package or missing using for a referenced project) — likely wrong test project folder or missing test .csproj update.
            - Flag failing builds/tests as blocker or high.

            {JsonOutputSchema}
            """;
    }

    public static bool HasBlockingFindings(AgentResult? result)
    {
        if (result is null)
        {
            return true;
        }

        return result.Findings.Exists(f => f.Severity is FindingSeverity.Blocker or FindingSeverity.High);
    }

    private static bool TryParseAuditResponse(string raw, out string summary, out List<AgentFinding> findings)
    {
        summary = raw;
        findings = new List<AgentFinding>();
        string candidate = ExtractJsonCandidate(raw);
        try
        {
            using var document = JsonDocument.Parse(candidate);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (root.TryGetProperty("summary", out JsonElement summaryNode) && summaryNode.ValueKind == JsonValueKind.String)
            {
                summary = summaryNode.GetString() ?? raw;
            }

            if (root.TryGetProperty("findings", out JsonElement findingsNode) && findingsNode.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in findingsNode.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    string message = item.TryGetProperty("message", out JsonElement messageNode)
                        && messageNode.ValueKind == JsonValueKind.String
                        ? messageNode.GetString() ?? string.Empty
                        : string.Empty;
                    if (string.IsNullOrWhiteSpace(message))
                    {
                        continue;
                    }

                    string severityText = item.TryGetProperty("severity", out JsonElement severityNode)
                        && severityNode.ValueKind == JsonValueKind.String
                        ? severityNode.GetString() ?? "medium"
                        : "medium";

                    findings.Add(new AgentFinding
                    {
                        Severity = ParseSeverity(severityText),
                        Message = message.Trim()
                    });
                }
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static FindingSeverity ParseSeverity(string severityText) =>
        severityText.Trim().ToLowerInvariant() switch
        {
            "blocker" => FindingSeverity.Blocker,
            "high" => FindingSeverity.High,
            "low" => FindingSeverity.Low,
            _ => FindingSeverity.Medium
        };

    private static string ExtractJsonCandidate(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return raw;
        }

        Match fenced = Regex.Match(raw, "```(?:json)?\\s*(\\{[\\s\\S]*\\})\\s*```", RegexOptions.IgnoreCase);
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

    private static string BuildBuildValidationRules(WorkflowState state)
    {
        if (state.BuildValidation is null)
        {
            return "Build/test validation: not run yet.";
        }

        bool? testsPassed = state.BuildValidation.TestsPassed;
        string testsLine = testsPassed switch
        {
            true => "Automated tests: passed (dotnet test).",
            false => "Automated tests: FAILED — add finding blocker or high.",
            _ => "Automated tests: not executed — add finding high if test projects exist in RAG/solution."
        };

        string buildLine = state.BuildValidation.ProductionBuildPassed == true
            ? "Production build: passed."
            : "Production build: failed — add finding blocker or high.";

        return $"""
            Build/test validation:
            - {buildLine}
            - {testsLine}
            - Summary: {state.BuildValidation.Summary}
            """;
    }

    protected override IReadOnlyList<AgentFinding> BuildFallbackFindings()
    {
        return
        [
            new AgentFinding
            {
                Severity = FindingSeverity.Medium,
                Message = "Audit ran in fallback mode; run explicit regression tests before merge."
            }
        ];
    }
}
