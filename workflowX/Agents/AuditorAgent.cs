using workflowX.Infrastructure;
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
        string buildStatus = BuildBuildValidationRules(state);

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

            {buildStatus}

            Return findings with severity labels: blocker/high/medium/low.
            Flag missing unit tests as high severity when new production code is introduced without a matching *Tests.cs file in the same test folders and naming style already used in this repository (repository, service, controller, domain, or other layers—discover from exemplars, not hard-coded names).
            Treat failing or unrun tests as release blockers when the solution includes test projects.
            Flag missing DI/bootstrap registration as high severity only for interface+implementation pairs newly introduced in proposed files that are not appended in the test/bootstrap file.
            Flag registrations for interfaces already wired in bootstrap/composition-root files or protected contracts as high severity.
            Flag rewriting or removing pre-existing DI registrations (InMemory/factory/lambda patterns for infrastructure already wired in the bootstrap file) as high severity — agents must append, not replace.
            Flag any change to pre-existing interface/store contracts (adding SaveChanges, Update, DbContext-style APIs) as blocker — new code must adapt to existing store interfaces only.
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
        var conventions = TestCoverageAuditor.DiscoverTestConventions(state.RepoPath);
        if (conventions.Count == 0)
        {
            return "Test coverage rules: no *Tests.cs convention detected in target repository.";
        }

        var lines = conventions
            .Select(c => $"- {c.TestDirectory}: *{Path.GetFileNameWithoutExtension(c.ProductionFileSuffix)} → {{Name}}Tests.cs ({c.ExemplarCount} exemplar(s))")
            .ToList();

        return $"""
            Test coverage rules (discovered from repository):
            {string.Join('\n', lines)}
            - For each new production file that matches a discovered layer suffix, require a sibling test file named <ProductionBaseName>Tests.cs under the same test folder.
            - Mirror existing test framework patterns (e.g. MSTest/xUnit/NUnit) from exemplar *Tests.cs files.
            - Treat missing tests as high severity when exemplars already exist for that layer.
            """;
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
            false => "Automated tests: FAILED—treat as blocker/high until all tests pass.",
            _ => "Automated tests: not executed (no test projects detected or skipped)."
        };

        string buildLine = state.BuildValidation.ProductionBuildPassed == true
            ? "Production build: passed."
            : "Production build: failed or not verified.";

        return $"""
            Build/test validation (deterministic):
            - {buildLine}
            - {testsLine}
            - Summary: {state.BuildValidation.Summary}
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
