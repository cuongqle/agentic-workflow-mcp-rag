using workflowX.Application;
using workflowX.Workflow;

namespace workflowX;

internal static class Program
{
    static async Task Main(string[] args)
    {
        var finalState = await new ApplicationHost().RunAsync(args);
        WorkflowResultPrinter.Print(finalState);
    }
}

