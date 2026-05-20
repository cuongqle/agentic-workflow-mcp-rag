using agents_mcp_rag.Configuration;
using Microsoft.SemanticKernel;

namespace agents_mcp_rag.Infrastructure;

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
