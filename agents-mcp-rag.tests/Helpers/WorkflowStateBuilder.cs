using agents_mcp_rag.Infrastructure;
using agents_mcp_rag.Infrastructure.Compliance.DotNet;

namespace agents_mcp_rag.Tests.Helpers;

internal static class WorkflowStateBuilder
{
    public static WorkflowState Create(
        string repoPath,
        RepoContract? contract = null,
        RepoStack? stack = null)
    {
        contract ??= MinimalContract(repoPath, stack ?? RepoStack.None);
        return new WorkflowState
        {
            RepoPath = repoPath,
            Contract = contract,
            Task = new WorkflowTask { Title = "test", Description = "test task" }
        };
    }

    public static RepoContract MinimalContract(string repoPath, RepoStack stack) =>
        new()
        {
            RepoPath = repoPath,
            PathRules =
            [
                new PathPlacementRule(
                    "Repository.cs",
                    "src/Repositories",
                    fileName => !fileName.StartsWith('I') && !fileName.Equals("Repository.cs", StringComparison.OrdinalIgnoreCase)),
                new PathPlacementRule(
                    "Repository.cs",
                    "src/Interfaces",
                    fileName => fileName.StartsWith('I') && !fileName.Equals("IRepository.cs", StringComparison.OrdinalIgnoreCase))
            ],
            Frontend = stack.Frontend
                ? new FrontendModuleTemplate(
                    "web/modules",
                    "web",
                    "employee",
                    FrontendLayoutMode.HostModulePages,
                    ["web/legacy"],
                    ["controllers", "views"],
                    ["module.js"],
                    [])
                : null,
            LayerConventions = stack.DotNet
                ? new LayerConventionProfiles(
                [
                    new LayerConventionProfile(
                        "repository",
                        "Repository.cs",
                        2,
                        "src/Repositories",
                        true,
                        true,
                        false,
                        [],
                        [],
                        LayerInterfacePairingConvention.None)
                ])
                : LayerConventionProfiles.Empty
        };

    public static void WithBuildFindings(WorkflowState state, params AgentFinding[] findings) =>
        WithBuildFindings(state, productionBuildPassed: null, findings);

    public static void WithBuildFindings(
        WorkflowState state,
        bool? productionBuildPassed,
        params AgentFinding[] findings)
    {
        state.BuildValidation = new AgentResult
        {
            AgentName = "BuildValidationAgent",
            Summary = "test",
            ProductionBuildPassed = productionBuildPassed,
            Findings = findings.ToList()
        };
    }
}
