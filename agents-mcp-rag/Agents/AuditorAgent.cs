using agents_mcp_rag.Infrastructure;
using Microsoft.SemanticKernel;

sealed class AuditorAgent : LlmWorkflowAgentBase
{
    public AuditorAgent(Kernel kernel) : base(kernel)
    {
    }

    public override string Name => "AuditorAgent";

    protected override string BuildPrompt(WorkflowState state)
    {
        string testCoverageRules = BuildTestCoverageRules(state);

        return $"""
            You are the auditor agent.
            Review for bugs, regressions, security risks, and missing tests.
            Focus on practical release-readiness.

            Architecture:
            {state.Architecture?.Summary}

            Backend:
            {state.Backend?.Summary}

            Frontend:
            {state.Frontend?.Summary}

            Observer:
            {state.Observer?.Summary}

            Unified RAG context:
            {state.CombinedRagContext}

            {testCoverageRules}

            Return findings with severity labels: blocker/high/medium/low.
            Flag missing repository unit tests as high severity when a new *Repository.cs is introduced without a matching *RepositoryTests.cs in the existing RepositoryTest folder.
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

    private static string BuildTestCoverageRules(WorkflowState state)
    {
        string? testsDir = TestCoverageAuditor.GetRepositoryTestsDirectory(state.RepoPath);
        if (string.IsNullOrWhiteSpace(testsDir))
        {
            return "Test coverage rules: no RepositoryTest convention detected in target repository.";
        }

        return $"""
            Test coverage rules (deterministic):
            - For every new <Entity>Repository.cs, require <Entity>RepositoryTests.cs under {testsDir}.
            - Mirror existing MSTest patterns ([TestClass], [TestMethod], bootstrap wiring) from sibling repository tests.
            - Treat missing repository tests as release blockers when RepositoryTest examples already exist.
            """;
    }

    protected override IReadOnlyList<AgentFinding> BuildFallbackFindings()
    {
        return new List<AgentFinding>
        {
            new()
            {
                Severity = FindingSeverity.Medium,
                Message = "Audit ran in fallback mode; run explicit regression tests before merge."
            }
        };
    }
}
