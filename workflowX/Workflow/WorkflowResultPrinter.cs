namespace workflowX.Workflow;

internal static class WorkflowResultPrinter
{
    public static void Print(WorkflowState finalState)
    {
        Console.WriteLine("\n=== Workflow Result ===");
        Console.WriteLine($"Final Stage: {finalState.Stage}");
        Console.WriteLine($"Architecture Agent: {finalState.Architecture?.Summary}");
        Console.WriteLine($"Backend Agent: {finalState.Backend?.Summary}");
        Console.WriteLine($"Frontend Agent: {finalState.Frontend?.Summary}");
        Console.WriteLine($"Build Validation Agent: {finalState.BuildValidation?.Summary}");
        Console.WriteLine($"Observer Agent: {finalState.Observer?.Summary}");
        Console.WriteLine($"Auditor Agent: {finalState.Audit?.Summary}");
        if (finalState.Recovery is not null)
        {
            Console.WriteLine($"Recovery Agent: {finalState.Recovery.Summary}");
        }
        if (!string.IsNullOrWhiteSpace(finalState.PullRequestStatus))
        {
            Console.WriteLine($"PR Status: {finalState.PullRequestStatus}");
        }
        if (!string.IsNullOrWhiteSpace(finalState.PullRequestUrl))
        {
            Console.WriteLine($"PR URL: {finalState.PullRequestUrl}");
        }

        Console.WriteLine("\n=== Timeline ===");
        foreach (var timelineEntry in finalState.Timeline)
        {
            Console.WriteLine(timelineEntry);
        }
    }
}
