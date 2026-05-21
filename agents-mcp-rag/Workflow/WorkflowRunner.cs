using agents_mcp_rag.Configuration;
using agents_mcp_rag.Infrastructure;
using Microsoft.SemanticKernel;
using ModelContextProtocol.Client;

namespace agents_mcp_rag.Workflow;

internal sealed class WorkflowRunner
{
    public async Task<WorkflowState> RunAsync(
        AppSettings settings,
        string[] args,
        Kernel kernel,
        McpClient mcpClient,
        CancellationToken cancellationToken = default)
    {
        string repoPath = RepositoryResolver.Prepare(settings.RepoPath);

        Console.WriteLine("\n=== Step 2: Building Local RAG Pipeline ===");
        var ragOptions = new RagBuildOptions(
            settings.UseHybridRag,
            settings.OpenAIKey,
            settings.OpenAIEmbeddingModel,
            settings.RagLexicalWeight,
            settings.RagVectorWeight);

        var ragIndex = await CodebaseRagIndex.BuildAsync(repoPath, ragOptions);
        if (ragIndex.TotalFiles > 0)
        {
            Console.WriteLine($"RAG index build complete. Files: {ragIndex.TotalFiles}, chunks: {ragIndex.TotalChunks}");
        }
        else
        {
            Console.WriteLine($"[Warning] Path {repoPath} not found. Please clone the repository locally first.");
        }

        Console.WriteLine("\n=== Step 4: Running Multi-Agent Development Workflow ===");
        string taskPrompt = args.Length > 0 ? string.Join(' ', args) : settings.DefaultTaskPrompt;
        RepoContract contract = RepoContractDiscoverer.Discover(repoPath);
        RagContextBundle ragContext = await RagContextComposer.BuildAsync(repoPath, taskPrompt, ragIndex, contract);

        var workflowState = new WorkflowState
        {
            RepoPath = repoPath,
            Contract = contract,
            ProjectStructureContext = ragContext.StructureContext,
            LegacyImplementationContext = ragContext.LegacyImplementationContext,
            CombinedRagContext = ragContext.CombinedContext,
            Task = new WorkflowTask
            {
                Title = "New Development Task",
                Description = taskPrompt
            }
        };

        var orchestrator = new WorkflowOrchestrator(
            new ArchitectureAgent(kernel),
            new ObserverAgent(kernel),
            new BackendDeveloperAgent(kernel),
            new FrontendDeveloperAgent(kernel),
            new BuildValidationAgent(),
            new AuditorAgent(kernel),
            new RecoveryAgent(kernel),
            new GitHubMcpAdapter(mcpClient, settings.AutoCreatePullRequest, settings.PullRequestBaseBranch),
            settings.MaxRecoveryAttempts,
            settings.MaxCompilationFixAttempts,
            settings.CompilationFixContext);

        return await orchestrator.RunAsync(workflowState, cancellationToken);
    }
}
