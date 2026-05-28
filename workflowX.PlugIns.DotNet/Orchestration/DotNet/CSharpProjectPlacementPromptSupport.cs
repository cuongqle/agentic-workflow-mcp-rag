namespace workflowX.Infrastructure;

/// <summary>
/// Shared prompt guidance: place new C# artifacts in existing solution projects (no invented sibling projects).
/// </summary>
internal static class CSharpProjectPlacementPromptSupport
{
    internal static IEnumerable<string> BuildRuleLines()
    {
        yield return
            "Use only solution project directory names from RAG; copy exemplar paths per layer and change only the file name.";
    }
}
