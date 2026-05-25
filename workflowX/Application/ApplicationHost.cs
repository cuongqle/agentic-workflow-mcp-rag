using workflowX.Configuration;
using workflowX.Infrastructure;
using workflowX.Workflow;
using Microsoft.SemanticKernel;
using ModelContextProtocol.Client;

namespace workflowX.Application;

internal sealed class ApplicationHost
{
    public async Task<WorkflowState> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        StackModuleRegistration.RegisterDefaults();

        var settings = AppSettingsLoader.Load();
        Kernel kernel = KernelFactory.Create(settings);
        await using McpClient mcpClient = await GitHubMcpClientFactory.ConnectAsync(settings.GitHubPat, cancellationToken);
        return await new WorkflowRunner().RunAsync(settings, args, kernel, mcpClient, cancellationToken);
    }
}
