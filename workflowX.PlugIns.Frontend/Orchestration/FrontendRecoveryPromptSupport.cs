namespace workflowX.Infrastructure;

/// <summary>
/// Stack-specific recovery prompt rules for frontend (TypeScript/JavaScript) deliverables.
/// </summary>
internal static class FrontendRecoveryPromptSupport
{
    internal static IEnumerable<string> BuildRuleLines()
    {
        yield return
            "For frontend apply rejections, read each reason literally and fix paths, imports, and module layout to match repository conventions.";
        yield return
            "When RAG or architecture context shows module exemplars, mirror their folder layout, exports, and import style.";
        yield return
            "Return complete, valid TypeScript or JavaScript source files only (balanced delimiters; no truncation).";
        yield return
            "Include required import directives.";
    }
}
