using agents_mcp_rag.Infrastructure;
using agents_mcp_rag.Infrastructure.BuildValidation.DotNet;

sealed class BuildValidationAgent : IWorkflowAgent
{
    public string Name => "BuildValidationAgent";

    public Task<AgentResult> ExecuteAsync(WorkflowState state, CancellationToken cancellationToken = default)
    {
        RepoContract contract = state.Contract ?? RepoContractDiscoverer.Discover(state.RepoPath);
        RepoStack stack = contract.Stack;

        if (!stack.DotNet && !stack.Frontend)
        {
            return Task.FromResult(new AgentResult
            {
                AgentName = Name,
                Summary = "Build validation skipped: no supported stack detected.",
                ProductionBuildPassed = null,
                Findings =
                {
                    new AgentFinding
                    {
                        Severity = FindingSeverity.Medium,
                        Message = "Build validation skipped: repository has no .NET or frontend build targets."
                    }
                }
            });
        }

        var partialResults = new List<AgentResult>();
        stack.WhenDotNet(() => partialResults.Add(DotNetBuildValidationSupport.Validate(state.RepoPath)));
        stack.WhenFrontend(() => partialResults.Add(FrontendBuildValidationSupport.Validate(state.RepoPath, contract)));

        return Task.FromResult(MergeResults(partialResults));
    }

    private static AgentResult MergeResults(IReadOnlyList<AgentResult> results)
    {
        if (results.Count == 1)
        {
            AgentResult single = results[0];
            return new AgentResult
            {
                AgentName = "BuildValidationAgent",
                Summary = single.Summary,
                ProductionBuildPassed = single.ProductionBuildPassed,
                TestsPassed = single.TestsPassed,
                Findings = single.Findings
            };
        }

        var findings = new List<AgentFinding>();
        var summaries = new List<string>();
        bool? productionPassed = null;
        bool? testsPassed = null;

        foreach (AgentResult result in results)
        {
            findings.AddRange(result.Findings);
            if (!string.IsNullOrWhiteSpace(result.Summary))
            {
                summaries.Add(result.Summary);
            }

            productionPassed = MergePassFlag(productionPassed, result.ProductionBuildPassed);
            testsPassed = MergePassFlag(testsPassed, result.TestsPassed);
        }

        return new AgentResult
        {
            AgentName = "BuildValidationAgent",
            Summary = string.Join(" ", summaries),
            ProductionBuildPassed = productionPassed,
            TestsPassed = testsPassed,
            Findings = findings
        };
    }

    private static bool? MergePassFlag(bool? current, bool? next)
    {
        if (next is null)
        {
            return current;
        }

        if (current is null)
        {
            return next;
        }

        return current.Value && next.Value;
    }
}
