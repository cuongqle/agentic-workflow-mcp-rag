namespace workflowX.Infrastructure;

/// <summary>
/// Shared C# prompt rules for architecture, implementation, and recovery agents.
/// </summary>
internal static class CSharpPromptSupport
{
    internal static IEnumerable<string> BuildArchitectureAgentRuleLines() =>
        BuildArchitecturePlanningRuleLines()
            .Concat(BuildArchitectureExemplarKindRuleLines())
            .Concat(BuildPlacementRuleLines())
            .Concat(BuildRecoveryApplyPathRuleLines())
            .Concat(BuildTestArtifactRuleLines())
            .Concat(BuildTestProjectConstraintRuleLines());

    internal static IEnumerable<string> BuildArchitectureExemplarKindRuleLines()
    {
        yield return
            "Before planning a new domain type, search RAG for how existing domain types are already modeled (source file kind, project folder, subfolders) and plan the same kind — change only the type name.";
        yield return
            "When RAG shows domain types as .cs class files in a project folder, plan new domain types as .cs files in that same path pattern — do not substitute migrations, SQL scripts, schema files, mapping/configuration classes, or a different project unless RAG shows that kind for comparable features.";
        yield return
            "Task wording about database, tables, relationships, or a specific storage technology does not override on-disk patterns; follow the exemplar source paths listed in RAG, not a greenfield persistence or data-access design.";
        yield return
            "Plan each deliverable kind only when RAG lists a same-kind source file under Production source exemplars or semantic hits — copy that exemplar path and change only the feature name segment.";
        yield return
            "Never plan migration, seed, mapping, store-configuration, or schema-only files unless RAG contains a same-kind file used for a sibling feature in this repo.";
        yield return
            "If Production source exemplars and semantic hits show no file like the one you would invent (no matching folder, suffix, or project segment), omit that backendFiles entry entirely — do not plan it.";
        yield return
            "For API controllers, find an existing *Controller.cs exemplar under Production source exemplars and plan the new controller at that same project folder and subfolders — change only the controller name in the file name.";
        yield return
            "Never plan a controller under a dotted namespace folder, under the repository root, or under a different solution project than the controller exemplar in RAG.";
    }

    internal static IEnumerable<string> BuildPlacementAndTestRuleLines() =>
        BuildPlacementRuleLines()
            .Concat(BuildTestArtifactRuleLines())
            .Concat(BuildTestProjectConstraintRuleLines())
            .Concat(BuildTestReferenceAndUsingsRuleLines())
            .Concat(BuildGeneratedArtifactExclusionRuleLines());

    internal static IEnumerable<string> BuildArchitecturePlanningRuleLines()
    {
        yield return
            "Plan one backendFiles.path per deliverable the task requires; each path must mirror a same-kind source file already shown in RAG.";
        yield return
            "Copy the exemplar's full repo-relative path (solution project directory and every subfolder segment); change only the file name.";
        yield return
            "Each artifact kind uses its own exemplar — do not place one kind's new file in another kind's project or folder even if names look related.";
        yield return
            "Use only solution project and folder segments that appear in RAG for that kind; do not invent new projects or directory names.";
    }

    internal static IEnumerable<string> BuildPlacementRuleLines()
    {
        yield return
            "Use only solution project directory names from RAG; copy exemplar paths per layer and change only the file name.";

        foreach (string rule in BuildProductionPlacementRuleLines())
        {
            yield return rule;
        }
    }

    internal static IEnumerable<string> BuildProductionPlacementRuleLines()
    {
        yield return
            "A solution project from RAG is exactly one directory segment in the repo-relative path (ProjectFolder/subfolders/File.cs) — never create a dotted folder such as ProjectName.Layer.Subfolder under a different parent project.";
        yield return
            "Do not confuse C# namespace with folder path: namespace dots do not become extra directories; copy the on-disk folder segments from the exemplar file path in RAG.";
        yield return
            "For each production artifact kind, copy the full repo-relative path of a same-kind exemplar and change only the type/file name segment.";
        yield return
            "Never place production layer files inside the repository root folder, a shared library folder, or another project's directory unless an exemplar for that layer already uses that location.";
    }

    internal static IEnumerable<string> BuildGeneratedArtifactExclusionRuleLines()
    {
        yield return
            "Never return AssemblyInfo.cs, *.AssemblyInfo.cs, GlobalUsings.cs, or any file under obj/ or bin/ — modern SDK projects generate assembly attributes automatically.";
        yield return
            "Never emit [assembly: AssemblyCompany/Title/Version/Product/Copyright/...] in any .cs file — the SDK generates those attributes; duplicates cause build failures.";
        yield return
            "When build output reports duplicate assembly attributes, remove duplicate [assembly: ...] or Assembly* attributes from hand-written source while the SDK also generates them — do not edit or create assembly info generated files.";
    }

    internal static IEnumerable<string> BuildTestArtifactRuleLines()
    {
        yield return
            "Test file and test class names must follow the same pattern as an existing same-layer *Tests.cs exemplar in RAG — do not invent a new naming scheme.";
        yield return
            "Copy the exemplar test file name and class name structure; change only the feature word that differs from production (keep the same name suffix pattern as the exemplar).";
        yield return
            "The public test class name must match the file name (without .cs), same as sibling tests in the repo.";
        yield return
            "Never prefix test file or class names with I — follow the concrete-type naming pattern from the exemplar test class.";
        yield return
            "Copy the exemplar's full repo-relative path (test project folder and subfolders); change only the file name segment that identifies the new feature.";
        yield return
            "Do not place tests under production source folders unless an on-disk test exemplar for that layer already uses that folder.";
    }

    internal static IEnumerable<string> BuildBackendImplementerScopeRuleLines()
    {
        yield return
            "Return ONLY paths listed in BACKEND_FILES (plus companion I* interface files for planned concrete implementations on the checklist) — apply rejects any other path as an unexpected deliverable.";
        yield return
            "Do NOT infer, invent, or return test files, test .csproj, production .csproj, mapping/store/persistence files, or extra production files that are not on the BACKEND_FILES checklist.";
        yield return
            "If tests or package references are required but no *Tests.cs or test .csproj appears on the checklist, implement only the listed paths and state in summary that architecture must add the missing entries.";
        yield return
            "Use each checklist path exactly as written — character-for-character match; apply rejects paths with an extra leading repository or solution folder segment.";
        yield return
            "The path in files[].path must equal the checklist entry (after normalizing slashes) — do not return a similar filename in a different project folder.";
    }

    internal static IEnumerable<string> BuildArchitectureTestPlanningRuleLines()
    {
        yield return
            "backendFiles is the complete allow-list for BackendDeveloperAgent — every path the implementer may return must appear in backendFiles.";
        yield return
            "When the task requires tests, list every *Tests.cs path AND the existing test .csproj from RAG in backendFiles (same batch — implementer will not create unlisted test files or .csproj).";
        yield return
            "Use only existing test project and test .csproj paths from RAG — never invent a new test project folder; include the .csproj when tests need PackageReference or ProjectReference updates.";
    }

    internal static IEnumerable<string> BuildTestProjectConstraintRuleLines()
    {
        yield return
            "*Tests.cs must be planned and implemented only under a repo-relative path whose owning .csproj is an on-disk test project (listed in RAG \"Test project references\" with test SDK or test-framework packages) — never under a production (non-test) project folder.";
        yield return
            "For each layer's tests, copy the full path of an existing same-layer *Tests.cs exemplar from RAG (same test project directory and subfolders); change only the feature/type name segment — do not place tests next to the production source file they exercise.";
        yield return
            "Use only test project paths already present in RAG — never invent a new test solution project folder.";
    }

    internal static IEnumerable<string> BuildRecoveryApplyPathRuleLines()
    {
        yield return
            "Use repo-relative paths exactly as shown in Allowed files, RAG \"Test project references\", or build output — never prefix paths with an extra copy of the repository or solution root folder.";
        yield return
            "When build errors name a *Tests.cs file, you may also return that test project's .csproj using the exact path from RAG (same entry as Test project references), even if build output does not list the .csproj.";
        yield return
            "If apply rejected a .csproj overwrite, fix the path (remove duplicated folder segments) and return the .csproj at the path RAG lists for that test project.";
    }

    internal static IEnumerable<string> BuildTestReferenceAndUsingsRuleLines()
    {
        yield return
            "Every *Tests.cs return must be paired with the owning test .csproj (full file) when the test uses NuGet test libraries or types from referenced production projects — copy PackageReference and ProjectReference lines verbatim from RAG exemplars.";
        yield return
            "Return the test .csproj at the exact repo-relative path from RAG \"Test project references\" — not a path with an extra leading solution or repository folder segment.";
        yield return
            "When build output says a type or namespace from a NuGet package could not be found, the failing *Tests.cs is not built by a .csproj that includes that PackageReference — fix the exemplar test .csproj from RAG, not a production .csproj and not a newly invented test project.";
        yield return
            "If RAG shows a package on the exemplar test .csproj, your returned test .csproj must include the same PackageReference Include and Version (or central-package pattern) even when you believe the reference already exists.";
        yield return
            "When the test .csproj already has a ProjectReference to another project, still add every using required for types used in the test — copy using blocks from a sibling *Tests.cs exemplar in RAG, or from the namespace declared in that type's .cs file in Exemplar sources.";
        yield return
            "When build output says a type or namespace from a referenced project could not be found, open that type's definition in Exemplar sources and add `using <exact namespace>;` at the top of the test file — ProjectReference does not import namespaces.";
        yield return
            "Mirror the full using block from a sibling *Tests.cs exemplar for the same layer before writing new test code.";
        yield return
            "Before returning, verify each using in *Tests.cs maps to a PackageReference or ProjectReference on the same test .csproj you return; missing usings for referenced interfaces are not fixed by ProjectReference alone.";
    }

    internal static IEnumerable<string> BuildRecoveryRuleLines()
    {
        yield return
            "For C# apply rejections, read each reason literally (e.g. missing constructor dependency type 'IX') and add that dependency to the constructor.";
        yield return
            "When a rejection cites a layer exemplar or missing dependency, open the exemplar source in Exemplar sources and mirror its constructor signature for the target entity.";
        yield return
            "Match C# exemplar sources (constructors, interfaces, namespaces, file layout).";

        foreach (string rule in BuildPlacementRuleLines())
        {
            yield return rule;
        }

        foreach (string rule in BuildTestArtifactRuleLines())
        {
            yield return rule;
        }

        foreach (string rule in BuildTestProjectConstraintRuleLines())
        {
            yield return rule;
        }

        foreach (string rule in BuildTestReferenceAndUsingsRuleLines())
        {
            yield return rule;
        }

        foreach (string rule in BuildGeneratedArtifactExclusionRuleLines())
        {
            yield return rule;
        }

        foreach (string rule in BuildRecoveryApplyPathRuleLines())
        {
            yield return rule;
        }

        yield return
            "Return complete, valid C# source files only (balanced braces; no truncation).";
        yield return
            "Never return placeholder content (TODO, NotImplementedException, 'Add methods ... if needed', or stub comments).";
        yield return
            "Never create a new test .csproj; update only an existing test project from RAG. Do not create other new .csproj files unless a build error explicitly requires it.";
        yield return
            "Include required using directives.";
        yield return
            "Use Parse/TryParse only for string sources; never parse values already typed as int/Guid/DateTime/etc.";
        yield return
            "For [FromQuery]/[FromRoute] string inputs, convert to dependency method parameter types before calling repositories/services (prefer TryParse + validation response).";
        yield return
            "Never use direct casts between unrelated identifier types (e.g. Guid to int); align boundary input types to the dependency contract type.";
        yield return
            "Do not modify protected existing infrastructure contracts (shared base abstractions already in the repo); adapt feature code to those contracts.";
        yield return
            "During recovery, only return full content for existing files that appear in the compiler error list; do not rewrite other on-disk files.";
        yield return
            "Do not rewrite existing repository/service interfaces on disk; adapt implementations and callers to match the current contract exactly.";
        yield return
            "Any class that declares an interface in its inheritance list must implement every interface member with matching signatures (including parameter CLR types).";
        yield return
            "When the same domain field/value flows across layers, keep one consistent type end-to-end and avoid mixed-type signatures.";
        yield return
            "When a rejection says a method is not declared on an interface, replace the call with one of that interface's known members exactly.";
        yield return
            "Never call methods that are not declared on the injected interface type; verify method name and parameter types against the on-disk interface contract before returning output.";
        yield return
            "For generic base repositories, keep closed interface contracts concrete (e.g. Insert(TEntity)); do not degrade signatures to open generic placeholders (Insert(T)).";
        yield return
            "For DI bootstrappers/composition roots, only append interface-to-implementation pairs; never add concrete-only registrations and never remove existing lines.";
        yield return
            "When updating DI registrations, modify only existing registration/composition-root blocks; do not place Add* registrations in tests/controllers/reset/init helpers or unrelated methods.";
        yield return
            "If a required DI fix cannot be located in an obvious existing registration block, stop and report low confidence instead of guessing file/placement.";
        yield return
            "For POST/PUT/PATCH actions that accept foreign-key identifiers, resolve each *Id through the injected dependency for that role before persist/update, and return NotFound (or equivalent) when related records are missing.";
        yield return
            "When build output reports a missing type or namespace in tests, update the owning test .csproj using PackageReference/ProjectReference from RAG \"Test project references\".";
        yield return
            "In tests, never assign quoted string literals to non-string model/entity temporal properties; use correctly typed DateTime/DateTimeOffset/DateOnly/TimeOnly values based on on-disk model definitions.";
        yield return
            "Treat *Tests.cs files and any source file whose owning .csproj is a test project (Microsoft.NET.Test.Sdk, IsTestProject, or test Sdk) as test artifacts; fix production compile errors first, then test/bootstrap/DI failures.";
        yield return
            "Do not skip, quarantine, or defer failing test projects when production code compiles; keep fixing until the full solution build passes.";
        yield return
            "For each new production layer file, add or update matching <Subject>Tests.cs in the same test folder/naming style used by sibling exemplars.";
    }
}
