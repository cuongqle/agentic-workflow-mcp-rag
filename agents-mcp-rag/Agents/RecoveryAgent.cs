using Microsoft.SemanticKernel;

sealed class RecoveryAgent : LlmWorkflowAgentBase
{
    public RecoveryAgent(Kernel kernel) : base(kernel)
    {
    }

    public override string Name => "RecoveryAgent";

    protected override string BuildPrompt(WorkflowState state)
    {
        string allowedFiles = state.CompilationFixAllowedFiles.Count == 0
            ? "- none detected from findings (infer carefully)"
            : string.Join("\n", state.CompilationFixAllowedFiles.Select(path => $"- {path}"));
        string complianceIssues = state.ComplianceIssues.Count > 0
            ? string.Join("\n", state.ComplianceIssues.Select(i => $"- {i}"))
            : "- none";

        return $@"You are the recovery agent.
Goal: make the repository compile and satisfy blocking compliance constraints with minimal, safe edits.

Task:
{state.Task.Description}

Latest audit summary:
{state.Audit?.Summary}

Latest build validation summary:
{state.BuildValidation?.Summary}

Compliance/rejection findings:
{complianceIssues}

Allowed files to edit (strict when present):
{allowedFiles}

Unified RAG context:
{state.CombinedRagContext}

Current API contract context (authoritative declarations from repository):
{state.CompilationContractContext}

IMPORTANT: Return strictly valid JSON with this shape:
{{
  ""summary"": ""short recovery summary"",
  ""files"": [
    {{
      ""path"": ""relative/path/from/repo/root.ext"",
      ""content"": ""full fixed file content""
    }}
  ]
}}

Modify only files listed under 'Allowed files to edit' unless impossible.
Do not return prose or partial snippets; each file content must be complete source code.
Produce concrete code fixes (not only advice).
Preserve existing project conventions and repository contracts.
Always include required using/import directives explicitly; never rely on IDE auto-import.
For unresolved type/namespace errors, add missing using statements in the same file you fix.
For CS0535 ('does not implement interface member'), implement all missing interface members with compile-ready signatures and bodies.
For interface mismatch issues, fix interface + implementation + caller signatures together so contracts stay consistent.
Avoid duplicate files/classes for any layer; update canonical existing files instead of creating copies in different folders.
Treat every compiler finding as a required contract to satisfy before finishing.
When implementing interface or abstract/base members, match signatures exactly (name, parameters, generic args, nullability, and return type).
Do not call properties/methods on interface-typed dependencies unless those members are defined on that interface (or valid extension methods already used in the codebase).
If a member access is invalid for an abstraction, refactor to the existing pattern used by sibling classes in the same layer.
When multiple files participate in a contract (interface + class + caller), update all of them consistently in one response.
Never introduce or reference new types/interfaces/base classes unless they already exist in the repository or are included in the same response.
Never modify pre-existing interfaces or infrastructure/store files. Do not add SaveChanges, Update, or EF-style APIs — use only members already declared in authoritative contract context.
For generic/base constraints, ensure every type argument satisfies required constraints from existing base classes/interfaces.
Before returning, verify each changed file compiles against the contracts it depends on (interfaces, base classes, generic constraints, namespace imports).
Prefer adapting code to existing abstractions instead of assuming extra members exist on interfaces.
If a symbol is missing, either add the proper existing import/namespace or change implementation to use already-defined symbols and patterns.
When compliance findings mention missing unit tests, create the expected {{ProductionBaseName}}Tests.cs under the discovered test folder for that layer and mirror sibling *Tests.cs structure (framework attributes, usings, namespace, setup/teardown).
For test compile errors (CS1525/CS1002), rewrite the full test file using the closest *Tests.cs exemplar from contract context; do not emit malformed terminators like ';;' or ',;'.
For CS0019 (operator cannot be applied to mismatched types), align test literals and assertions with entity property types listed in contract context (e.g. parse temporal values instead of quoted strings).
For CS0122 (inaccessible member) in tests, use only the public bootstrap API shown in contract context exemplar Setup lines — never access private ServiceProvider or other non-public bootstrap members.
For CS1061 on index Map (entity missing member), either add the property to the entity class or remove it from the index projection — use only properties listed in authoritative entity contract context.
For CS1061 in *Tests.cs, change the call to a member that exists on the type under test (see production API in contract context) — do not invent method names.
When fixing compile errors, copy patterns from the implementation exemplars in contract context; do not invent APIs — use only members visible on interfaces/base classes shown there.
If compliance findings mention missing DI registration, do NOT return the bootstrap .cs file — omit it from files[] so the workflow can append into the registration scope described in contract context. Never rewrite bootstrap usings or namespace braces.
Never suggest or add registrations for interfaces already wired in bootstrap exemplars — only new interface+implementation pairs from this task's proposed files.
For CS0246 missing namespaces in tests, prefer mirroring exemplar *Tests.cs usings; workflow auto-runs dotnet add package when needed. Do not add Moq unless existing tests already use it.";
    }

    protected override IReadOnlyList<AgentFinding> BuildFallbackFindings()
    {
        return new List<AgentFinding>
        {
            new()
            {
                Severity = FindingSeverity.Low,
                Message = "Recovery plan was generated with fallback output."
            }
        };
    }
}
