using agents_mcp_rag.Application;
using agents_mcp_rag.Workflow;

namespace agents_mcp_rag;

internal static class Program
{
    static async Task Main(string[] args)
    {
        var finalState = await new ApplicationHost().RunAsync(args);
        WorkflowResultPrinter.Print(finalState);
    }
}
