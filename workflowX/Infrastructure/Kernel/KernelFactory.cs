using workflowX.Configuration;
using Microsoft.SemanticKernel;

namespace workflowX.Infrastructure;

public static class KernelFactory
{
    public static Kernel Create(AppSettings settings)
    {
        Console.WriteLine("=== Step 1: Initializing Semantic Kernel ===");
        return Kernel.CreateBuilder()
            .AddOpenAIChatCompletion(settings.OpenAIModel, settings.OpenAIKey)
            .Build();
    }
}
