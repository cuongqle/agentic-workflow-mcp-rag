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
            "Use paths exactly as listed in Allowed files, build output, or RAG module exemplars — never prefix with an extra copy of the repository or application root folder.";
        yield return
            "WRONG: RepoName/RepoName.ProjectFolder/.../file.spec.js — RIGHT: RepoName.ProjectFolder/.../file.spec.js (mirror an existing same-kind test path from RAG; change only the feature name).";
        yield return
            "Only overwrite an existing spec/module file when build output names that exact path; otherwise add a new spec at the exemplar folder from RAG.";
        yield return
            "When RAG or architecture context shows module exemplars, mirror their folder layout, exports, and import style.";
        yield return
            "Return complete, valid TypeScript or JavaScript source files only (balanced delimiters; no truncation).";
        yield return
            "Include required import directives.";
    }
}
