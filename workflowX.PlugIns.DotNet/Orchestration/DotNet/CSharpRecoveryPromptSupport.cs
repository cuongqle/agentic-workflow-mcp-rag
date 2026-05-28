namespace workflowX.Infrastructure;

/// <summary>
/// Stack-specific recovery prompt rules for .NET / C# backends.
/// </summary>
internal static class CSharpRecoveryPromptSupport
{
    internal static IEnumerable<string> BuildRuleLines()
    {
        yield return
            "For C# apply rejections, read each reason literally (e.g. missing constructor dependency type 'IX') and add that dependency to the constructor.";
        yield return
            "When a rejection cites a layer exemplar or missing dependency, open the exemplar source in Exemplar sources and mirror its constructor signature for the target entity.";
        yield return
            "Match C# exemplar sources (constructors, interfaces, namespaces, file layout).";
        yield return
            "Return complete, valid C# source files only (balanced braces; no truncation).";
        yield return
            "Do not create new .csproj files unless required.";
        yield return
            "Include required using directives.";
        yield return
            "Use Parse/TryParse only for string sources; never parse values already typed as int/Guid/DateTime/etc.";
        yield return
            "Do not modify protected existing infrastructure contracts (core store/repository/entity abstractions and shared base infrastructure definitions); adapt feature code to those contracts.";
        yield return
            "When a rejection says a method is not declared on an interface, replace the call with one of that interface's known members exactly.";
    }
}
