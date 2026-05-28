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
        foreach (string rule in CSharpProjectPlacementPromptSupport.BuildRuleLines())
        {
            yield return rule;
        }
        yield return
            "Return complete, valid C# source files only (balanced braces; no truncation).";
        yield return
            "Never return placeholder content (TODO, NotImplementedException, 'Add methods ... if needed', or stub comments).";
        yield return
            "Do not create new .csproj files unless required.";
        yield return
            "Include required using directives.";
        yield return
            "Use Parse/TryParse only for string sources; never parse values already typed as int/Guid/DateTime/etc.";
        yield return
            "For [FromQuery]/[FromRoute] string inputs, convert to dependency method parameter types before calling repositories/services (prefer TryParse + validation response).";
        yield return
            "Never use direct casts between unrelated identifier types (e.g. Guid to int); align boundary input types to the dependency contract type.";
        yield return
            "Do not modify protected existing infrastructure contracts (core store/repository/entity abstractions and shared base infrastructure definitions); adapt feature code to those contracts.";
        yield return
            "During recovery, only return full content for existing files that appear in the compiler error list; do not rewrite other on-disk files.";
        yield return
            "Do not rewrite existing repository/service interfaces on disk; adapt implementations and callers to match the current contract exactly.";
        yield return
            "Any class that declares an interface in its inheritance list must implement every interface member with matching signatures (including parameter CLR types).";
        yield return
            "When the same domain field/value flows across DTO, controller, service, repository, and entity layers, keep one consistent type end-to-end and avoid mixed-type signatures.";
        yield return
            "When a rejection says a method is not declared on an interface, replace the call with one of that interface's known members exactly.";
        yield return
            "Never call methods that are not declared on the injected interface type; verify method name and parameter types against the on-disk interface contract before returning output.";
        yield return
            "For generic base repositories, keep closed interface contracts concrete (e.g. Insert(Timesheet)); do not degrade signatures to open generic placeholders (Insert(T)).";
        yield return
            "For DI bootstrappers/composition roots, only append interface-to-implementation pairs; never add concrete-only registrations and never remove existing lines.";
        yield return
            "When updating DI registrations, modify only existing registration/composition-root blocks; do not place Add* registrations in tests/controllers/reset/init helpers or unrelated methods.";
        yield return
            "If a required DI fix cannot be located in an obvious existing registration block, stop and report low confidence instead of guessing file/placement.";
        yield return
            "For POST/PUT/PATCH controller actions, resolve each foreign-key *Id through the corresponding injected role repository before persist/update, and return NotFound (or equivalent) when related records are missing.";
        yield return
            "For test compilation failures caused by missing packages/namespaces, mirror package references from sibling test projects and update the correct existing test .csproj minimally (avoid introducing new mocking libraries unless repo exemplars already use them).";
        yield return
            "In tests, never assign quoted string literals to non-string model/entity temporal properties; use correctly typed DateTime/DateTimeOffset/DateOnly/TimeOnly values based on on-disk model definitions.";
        yield return
            "Treat paths under *UnitTest*, *Tests*, *RepositoryTest*, files ending in Tests.cs, and *.Tests.csproj as test artifacts; fix production compile errors first, then test/bootstrap/DI failures.";
        yield return
            "Do not skip, quarantine, or defer failing test projects when production code compiles; keep fixing until the full solution build passes.";
        yield return
            "For each new production layer file, add or update matching <Subject>Tests.cs in the same test folder/naming style used by sibling exemplars.";
    }
}
