namespace workflowX.Infrastructure;

/// <summary>
/// Shared C# prompt rules for architecture, implementation, and recovery agents.
/// </summary>
internal static class CSharpPromptSupport
{
    internal static IEnumerable<string> BuildArchitectureAgentRuleLines() =>
        BuildArchitecturePlanningRuleLines()
            .Concat(BuildArchitectureExemplarKindRuleLines())
            .Concat(BuildExemplarMirrorAndContractRuleLines())
            .Concat(BuildPlacementRuleLines())
            .Concat(BuildRecoveryApplyPathRuleLines())
            .Concat(BuildArchitectureTestPlanningRuleLines())
            .Concat(BuildTestArtifactRuleLines())
            .Concat(BuildTestProjectConstraintRuleLines());

    internal static IEnumerable<string> BuildArchitectureExemplarKindRuleLines()
    {
        yield return
            "Before planning a new deliverable, search RAG for how same-kind source files are already modeled (file shape, project folder, subfolders) and plan the same kind — change only the feature name.";
        yield return
            "When RAG shows a deliverable as a source file in a project folder, plan new files in that same path pattern — do not substitute alternate technologies or file kinds unless RAG shows that pattern for comparable features.";
        yield return
            "Task and requirements may name databases, ORMs, stores, or 'context' APIs that do NOT appear in Production source exemplars or Exemplar sources — ignore those names entirely; they are not a pattern for this repo.";
        yield return
            "Never describe or plan 'following' a technology from the task (e.g. a named store or context type) unless the exact type name appears in a same-kind exemplar .cs file listed in RAG.";
        yield return
            "Plan each deliverable kind only when RAG lists a same-kind source file under Production source exemplars or semantic hits — copy that exemplar path and change only the feature name segment.";
        yield return
            "Never plan migration, seed, mapping, store-configuration, or schema-only files unless RAG contains a same-kind file used for a sibling feature in this repo.";
        yield return
            "If Production source exemplars and semantic hits show no file like the one you would invent (no matching folder, suffix, or project segment), omit that backendFiles entry entirely — do not plan it.";
        yield return
            "When a same-kind exemplar references other types that share its subject prefix with a different role suffix (visible in full exemplar source or RAG), list each corresponding path in backendFiles — copy the on-disk companion exemplar path and change only the subject segment.";
        yield return
            "Use RAG 'Grouped folder exemplars' to pick the parent folder: a new file must live in the same folder group as a same-kind on-disk exemplar — never in a different project folder than siblings of that kind.";
        yield return
            "Plan domain types and tests only using paths listed under Production source exemplars and Test source exemplars in RAG — do not invent new naming schemes.";
    }

    internal static IEnumerable<string> BuildPlacementAndTestRuleLines() =>
        BuildPlacementRuleLines()
            .Concat(BuildExemplarMirrorAndContractRuleLines())
            .Concat(BuildTestArtifactRuleLines())
            .Concat(BuildTestProjectConstraintRuleLines())
            .Concat(BuildTestReferenceAndUsingsRuleLines())
            .Concat(BuildGeneratedArtifactExclusionRuleLines());

    internal static IEnumerable<string> BuildArchitecturePlanningRuleLines()
    {
        yield return
            "backendFiles.description must be one short line: which same-kind exemplar path in RAG to mirror (change only the subject segment) — do not list invented types, stores, ORMs, or behaviors not visible in that exemplar file.";
        yield return
            "summary and testStrategy must not name storage products, drivers, or data-access types unless those exact identifiers appear in Production source exemplars in RAG.";
        yield return
            "Plan one backendFiles.path per deliverable the task requires; each path must mirror a same-kind source file already shown in RAG.";
        yield return
            "Copy the exemplar's full repo-relative path (solution project directory and every subfolder segment); change only the file name.";
        yield return
            "Each deliverable kind uses its own exemplar folder group in RAG — do not place one kind's new file in another group's project or folder even if names look related.";
        yield return
            "Use only solution project and folder segments that appear in RAG for that kind; do not invent new projects or directory names.";
    }

    internal static IEnumerable<string> BuildExemplarMirrorAndContractRuleLines()
    {
        yield return
            "Exemplar sources (full same-kind .cs files) are the only implementation spec — ignore architecture summaries, backendFiles descriptions, and task text for type names, stores, and APIs.";
        yield return
            "For every deliverable you implement or fix, read the full same-kind file in Exemplar sources first; copy the complete using block, namespace declaration, constructor injection, dependency calls, and control flow from that exemplar — do not substitute types from the task or requirements.";
        yield return
            "When Exemplar sources include 'Required usings and namespaces', copy those lines into the matching deliverable before any namespace or type declarations — ProjectReference alone does not import namespaces.";
        yield return
            "When one deliverable references another on BACKEND_FILES in a different folder or project, use the same `using` lines the same-kind exemplar uses for that cross-role type (see Required usings section).";
        yield return
            "Copy patterns verbatim from that exemplar — paths, constructor injection, calls on injected abstractions, async vs sync, and error handling — change only the feature/type name.";
        yield return
            "When the same-kind exemplar references cross-role types (other subject+role type names in its body), implement every such type that appears on BACKEND_FILES; if a referenced type is not on the checklist, do not reference it — state the gap in summary.";
        yield return
            "Before calling any method on an injected type, confirm that member exists on its definition in RAG; never assume framework or ORM helpers unless RAG shows them on that type.";
        yield return
            "Do not add, rename, or extend members on shared interfaces or base contracts already in the repo; adapt your code to the on-disk contract.";
        yield return
            "When build output says an injected type does not contain a definition for a method you used, stop using that method and substitute a member that exists in RAG, matching how same-kind exemplars call that dependency.";
        yield return
            "When build output says a member is not declared on an interface, remove or replace that call — do not add the member to the shared interface.";
    }

    internal static IEnumerable<string> BuildPlacementRuleLines()
    {
        yield return
            "Use only solution project directory names from RAG; copy exemplar paths per deliverable kind and change only the file name.";

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
            "Never place production deliverables inside the repo root folder, a shared library folder, or another project's directory unless a same-kind exemplar already uses that location.";
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
            "Test file and test class names must follow the same pattern as an existing same-kind test exemplar in RAG — do not invent a new naming scheme.";
        yield return
            "Copy the exemplar test file name and class name structure; change only the feature word that differs from production (keep the same name suffix pattern as the exemplar).";
        yield return
            "The public test class name must match the file name (without .cs), same as sibling tests in the repo.";
        yield return
            "Never prefix test file or class names with I — follow the concrete-type naming pattern from the exemplar test class.";
        yield return
            "Copy the exemplar's full repo-relative path (test project folder and subfolders); change only the file name segment that identifies the new feature.";
        yield return
            "Do not place tests under production source folders unless an on-disk test exemplar for that deliverable kind already uses that folder.";
    }

    internal static IEnumerable<string> BuildBackendImplementerScopeRuleLines()
    {
        yield return
            "Return ONLY paths listed in BACKEND_FILES (plus companion I* interface files for planned concrete implementations on the checklist) — apply rejects any other path as an unexpected deliverable.";
        yield return
            "Do NOT infer, invent, or return test files, test .csproj, production .csproj, or any production file that is not on the BACKEND_FILES checklist.";
        yield return
            "If tests or package references are required but no test source path or test project file appears on the checklist, implement only the listed paths and state in summary that architecture must add the missing entries.";
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
            "When the task requires tests, list every test source path AND the existing test project file from RAG in backendFiles (same batch — implementer will not create unlisted test or project files).";
        yield return
            "For each production deliverable in backendFiles, include the paired *Tests.cs exemplar path from RAG Test source exemplars (same test project folder segments and file-name pattern — change only the subject segment).";
        yield return
            "Test file and public test class names must match the repo exemplar pattern (*Tests.cs suffix, no I-prefix); copy the exemplar test path and change only the subject segment.";
        yield return
            "Use only existing test project and test .csproj paths from RAG — never invent a new test project folder; include the .csproj when tests need PackageReference or ProjectReference updates.";
    }

    internal static IEnumerable<string> BuildTestProjectConstraintRuleLines()
    {
        yield return
            "Test source files must be planned and implemented only under a repo-relative path whose owning project file is an on-disk test project (listed in RAG \"Test project references\" with test SDK markers) — never under a production project folder.";
        yield return
            "For each production deliverable that needs tests, copy the full path of an existing same-kind test exemplar from RAG (same test project directory and subfolders); change only the feature/type name segment — do not place tests next to unrelated production folders.";
        yield return
            "Use only test project paths already present in RAG — never invent a new test solution project folder.";
    }

    internal static IEnumerable<string> BuildRecoveryApplyPathRuleLines()
    {
        yield return
            "Use repo-relative paths exactly as shown in Allowed files, RAG \"Test project references\", or build output — never prefix paths with an extra copy of the repository or solution root folder.";
        yield return
            "WRONG: RepoName/RepoName.ProjectFolder/File.ext — RIGHT: RepoName.ProjectFolder/File.ext (same rule for .cs, .csproj, .js, .ts, and other extensions).";
        yield return
            "WRONG: RepoName.RepoName.ProjectFolder/File.ext (dot between repo and project) — use RepoName.ProjectFolder/File.ext only.";
        yield return
            "When build errors name a test source file, you may also return that test project's project file using the exact path from RAG (same entry as Test project references), even if build output does not list the project file.";
        yield return
            "When build errors name a production source file, you may return that owning project file at the path listed in Allowed files or RAG — never under RepoName/RepoName.Project/...";
        yield return
            "If apply rejected a path for duplicated folder prefix or missing compiler reference, return the file only at the canonical path from Allowed files or build output — do not re-send the rejected path.";
    }

    internal static IEnumerable<string> BuildTestReferenceAndUsingsRuleLines()
    {
        yield return
            "Every test source file return must be paired with the owning test project file (full file) when the test uses external packages or types from referenced production projects — copy package and project reference lines verbatim from RAG exemplars.";
        yield return
            "Return the test project file at the exact repo-relative path from RAG \"Test project references\" — not a path with an extra leading solution or repository folder segment.";
        yield return
            "When build output says a type or namespace from a package could not be found, the failing test source is not built by a project file that includes that package reference — fix the exemplar test project file from RAG, not a production project file and not a newly invented test project.";
        yield return
            "If RAG shows a package on the exemplar test project file, your returned test project file must include the same package reference and version (or central-package pattern) even when you believe the reference already exists.";
        yield return
            "When returning a test .csproj, copy <TargetFramework> exactly from RAG \"Test project references\" (same line as exemplar) — never change it; NU1201 means the TFM no longer matches a referenced production project.";
        yield return
            "When the test project file already references another project, still add every using required for types used in the test — copy using blocks from a sibling test exemplar in RAG, or from the namespace declared in that type's source in Exemplar sources.";
        yield return
            "When build output says a type or namespace from a referenced project could not be found, open that type's definition in Exemplar sources and add `using <exact namespace>;` at the top of the test file — ProjectReference does not import namespaces.";
        yield return
            "Mirror the full using block from a sibling test exemplar for the same deliverable kind before writing new test code.";
        yield return
            "Before returning, verify each using in test source maps to a package or project reference on the same test project file you return; missing usings for referenced types are not fixed by project reference alone.";
    }

    internal static IEnumerable<string> BuildRecoveryRuleLines()
    {
        yield return
            "For C# apply rejections, read each reason literally (e.g. missing constructor dependency type 'IX') and add that dependency to the constructor.";
        yield return
            "When a rejection cites a missing dependency or exemplar mismatch, open the same-kind exemplar in Exemplar sources and mirror its constructor and dependency usage.";
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

        foreach (string rule in BuildExemplarMirrorAndContractRuleLines())
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
            "For string boundary inputs from HTTP attributes ([FromQuery]/[FromRoute]/[FromBody]), convert to dependency method parameter types before calling injected dependencies (prefer TryParse + validation response).";
        yield return
            "Never use direct casts between unrelated identifier types (e.g. Guid to int); align boundary input types to the dependency contract type.";
        yield return
            "Do not modify protected existing infrastructure contracts (shared base abstractions already in the repo); adapt feature code to those contracts.";
        yield return
            "During recovery, only return full content for existing files that appear in the compiler error list; do not rewrite other on-disk files.";
        yield return
            "Any class that declares an interface in its inheritance list must implement every interface member with matching signatures (including parameter CLR types).";
        yield return
            "When the same domain field or value appears in multiple deliverables, keep one consistent type end-to-end and avoid mixed-type signatures.";
        yield return
            "For DI bootstrappers/composition roots, only append interface-to-implementation pairs; never add concrete-only registrations and never remove existing lines.";
        yield return
            "When updating DI registrations, modify only existing registration/composition-root blocks; do not place registrations in test files, unrelated helpers, or the wrong project.";
        yield return
            "If a required DI fix cannot be located in an obvious existing registration block, stop and report low confidence instead of guessing file/placement.";
        yield return
            "For mutation endpoints that accept foreign-key identifiers, resolve each *Id through the injected dependency used in same-kind exemplars before persist/update, and return NotFound (or equivalent) when related records are missing.";
        yield return
            "When build output reports a missing type or namespace in tests, update the owning test .csproj using PackageReference/ProjectReference from RAG \"Test project references\".";
        yield return
            "When build output reports NU1201 or project compatibility, restore the exemplar test .csproj TargetFramework and ProjectReference blocks from RAG verbatim — do not invent net5.0 or other legacy frameworks.";
        yield return
            "In tests, never assign quoted string literals to non-string temporal properties on model types; use correctly typed temporal values based on on-disk model definitions in RAG.";
        yield return
            "Treat test source files and any file whose owning project is a test project (per RAG test project references) as test artifacts; fix production compile errors first, then test/bootstrap/DI failures.";
        yield return
            "Do not skip, quarantine, or defer failing test projects when production code compiles; keep fixing until the full solution build passes.";
        yield return
            "For each new production deliverable, add or update matching tests in the same test folder and naming style used by same-kind exemplars in RAG.";
    }
}
