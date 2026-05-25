using workflowX.Configuration;
using workflowX.Infrastructure;
using Microsoft.SemanticKernel;
using ModelContextProtocol.Client;

namespace workflowX.Workflow;

internal sealed class WorkflowRunner
{
    public async Task<WorkflowState> RunAsync(
        AppSettings settings,
        string[] args,
        Kernel kernel,
        McpClient mcpClient,
        CancellationToken cancellationToken = default)
    {
        WorkflowCliArgs.ParsedArgs parsedArgs = WorkflowCliArgs.Parse(args, settings.DefaultTaskPrompt, settings.Resume);
        string repoPath = RepositoryResolver.Prepare(settings.RepoPath);
        string taskPrompt = parsedArgs.TaskPrompt;
        WorkflowResumeOptions resumeOptions = parsedArgs.ResumeOptions;

        Console.WriteLine("\n=== Step 1: Resolving workflow state ===");
        WorkflowState? workflowState = WorkflowStateCheckpointStore.TryLoad(repoPath, taskPrompt, resumeOptions);
        bool resumed = workflowState is not null;
        if (resumed)
        {
            Console.WriteLine($"Resuming from stage: {workflowState!.Stage}");
        }
        else if (resumeOptions.StartFromStage is WorkflowStage explicitStage)
        {
            Console.WriteLine($"Starting fresh workflow from explicit stage: {explicitStage}");
        }
        else
        {
            Console.WriteLine("Starting fresh workflow.");
        }

        Console.WriteLine("\n=== Step 2: Building Local RAG Pipeline ===");
        RepoContract contract = RepoContractDiscoverer.Discover(repoPath);
        var ragOptions = new RagBuildOptions(
            settings.UseHybridRag,
            settings.OpenAIKey,
            settings.OpenAIEmbeddingModel,
            settings.RagLexicalWeight,
            settings.RagVectorWeight);

        var ragIndex = await CodebaseRagIndex.BuildAsync(repoPath, ragOptions, contract);
        if (ragIndex.TotalFiles > 0)
        {
            Console.WriteLine($"RAG index build complete. Files: {ragIndex.TotalFiles}, chunks: {ragIndex.TotalChunks}");
        }
        else
        {
            Console.WriteLine($"[Warning] Path {repoPath} not found. Please clone the repository locally first.");
        }

        workflowState ??= new WorkflowState
        {
            RepoPath = repoPath,
            Contract = contract,
            Task = new WorkflowTask
            {
                Title = "New Development Task",
                Description = taskPrompt
            }
        };

        workflowState.RepoPath = repoPath;
        workflowState.Contract = contract;
        if (!string.IsNullOrWhiteSpace(taskPrompt))
        {
            workflowState.Task = new WorkflowTask
            {
                Title = string.IsNullOrWhiteSpace(workflowState.Task.Title)
                    ? "New Development Task"
                    : workflowState.Task.Title,
                Description = taskPrompt
            };
        }

        if (!resumed || string.IsNullOrWhiteSpace(workflowState.CombinedRagContext))
        {
            Console.WriteLine("\n=== Step 3: Composing RAG context ===");
            RagContextBundle ragContext = await RagContextComposer.BuildAsync(repoPath, taskPrompt, ragIndex, contract);
            workflowState.ProjectStructureContext = ragContext.StructureContext;
            workflowState.LegacyImplementationContext = ragContext.LegacyImplementationContext;
            workflowState.CombinedRagContext = ragContext.CombinedContext;
        }

        Console.WriteLine("\n=== Step 4: Running Multi-Agent Development Workflow ===");
        var orchestrator = new WorkflowOrchestrator(
            new RequirementsAgent(kernel),
            new ArchitectureAgent(kernel),
            new ObserverAgent(kernel),
            new BackendDeveloperAgent(kernel),
            new FrontendDeveloperAgent(kernel),
            new BuildValidationAgent(),
            new AuditorAgent(kernel),
            new RecoveryAgent(kernel),
            new AcceptanceCriteriaAgent(kernel),
            new GitHubMcpAdapter(mcpClient, settings.AutoCreatePullRequest, settings.PullRequestBaseBranch),
            settings.MaxRecoveryAttempts,
            settings.MaxCompilationFixAttempts,
            settings.CompilationFixContext,
            settings.AcceptanceCriteria);

        return await orchestrator.RunAsync(workflowState, resumeOptions, cancellationToken);
    }
}
