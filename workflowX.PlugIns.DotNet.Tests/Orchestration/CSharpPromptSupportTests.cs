using workflowX.Infrastructure;

namespace workflowX.PlugIns.DotNet.Tests.Orchestration;

public class CSharpPromptSupportTests
{
    [Fact]
    public void BuildArchitecturePlanningRuleLines_stays_exemplar_driven_without_sample_project_terms()
    {
        IEnumerable<string> lines = CSharpPromptSupport.BuildArchitecturePlanningRuleLines().ToList();
        string combined = string.Join(' ', lines);

        Assert.Contains("exemplar", combined, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("same-kind", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RavenDB", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("RavenStore", combined, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildArchitectureExemplarKindRuleLines_omits_unlisted_persistence_artifacts()
    {
        IEnumerable<string> lines = CSharpPromptSupport.BuildArchitectureExemplarKindRuleLines().ToList();
        string combined = string.Join(' ', lines);

        Assert.Contains("mapping", combined, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Production source exemplars", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("RavenDB", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("Timesheet", combined, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildTestArtifactRuleLines_requires_matching_existing_exemplar_pattern()
    {
        IEnumerable<string> lines = CSharpPromptSupport.BuildTestArtifactRuleLines().ToList();
        string combined = string.Join(' ', lines);

        Assert.Contains("exemplar", combined, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("same pattern", combined, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("class name must match the file name", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ConcreteType", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("<Entity>", combined, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildRecoveryRuleLines_includes_constructor_and_csproj_guidance()
    {
        List<string> lines = CSharpPromptSupport.BuildRecoveryRuleLines().ToList();

        Assert.Contains(lines, line => line.Contains("constructor dependency", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains(".csproj", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("Parse/TryParse", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("[FromQuery]/[FromRoute]", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("protected existing infrastructure contracts", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("not declared on an interface", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("TypeScript", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildTestProjectConstraintRuleLines_forbids_production_test_placement_and_new_csproj()
    {
        IEnumerable<string> lines = CSharpPromptSupport.BuildTestProjectConstraintRuleLines().ToList();
        string combined = string.Join(' ', lines);

        Assert.Contains("test project", combined, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never invent", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WebAPI", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("UnitTests", combined, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildProductionPlacementRuleLines_forbids_dotted_namespace_folders()
    {
        IEnumerable<string> lines = CSharpPromptSupport.BuildProductionPlacementRuleLines().ToList();
        string combined = string.Join(' ', lines);

        Assert.Contains("namespace", combined, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("directory", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WebAPI", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("SinglePageSample", combined, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildGeneratedArtifactExclusionRuleLines_forbids_assembly_info_files()
    {
        IEnumerable<string> lines = CSharpPromptSupport.BuildGeneratedArtifactExclusionRuleLines().ToList();
        string combined = string.Join(' ', lines);

        Assert.Contains("AssemblyInfo", combined, StringComparison.Ordinal);
        Assert.Contains("duplicate assembly", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CS0579", combined, StringComparison.Ordinal);
        Assert.Contains("obj/", combined, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildTestReferenceAndUsingsRuleLines_describes_missing_types_without_error_codes()
    {
        IEnumerable<string> lines = CSharpPromptSupport.BuildTestReferenceAndUsingsRuleLines().ToList();
        string combined = string.Join(' ', lines);

        Assert.Contains("could not be found", combined, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exact namespace", combined, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ProjectReference", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("CS0246", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("Moq", combined, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildArchitectureExemplarKindRuleLines_forbids_invented_migrations_without_hardcoded_layers()
    {
        IEnumerable<string> lines = CSharpPromptSupport.BuildArchitectureExemplarKindRuleLines().ToList();
        string combined = string.Join(' ', lines);

        Assert.Contains("migration", combined, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RAG", combined, StringComparison.Ordinal);
        Assert.Contains("domain types", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Db", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("Repository", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("Timesheet", combined, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildBackendImplementerScopeRuleLines_forbids_unlisted_deliverables()
    {
        IEnumerable<string> lines = CSharpPromptSupport.BuildBackendImplementerScopeRuleLines().ToList();
        string combined = string.Join(' ', lines);

        Assert.Contains("ONLY", combined, StringComparison.Ordinal);
        Assert.Contains("BACKEND_FILES", combined, StringComparison.Ordinal);
        Assert.Contains("Do NOT infer", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception:", combined, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildArchitectureTestPlanningRuleLines_requires_csproj_on_plan()
    {
        IEnumerable<string> lines = CSharpPromptSupport.BuildArchitectureTestPlanningRuleLines().ToList();
        string combined = string.Join(' ', lines);

        Assert.Contains("allow-list", combined, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("test .csproj", combined, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("*Tests.cs", combined, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildArchitectureAgentRuleLines_includes_planning_placement_and_test_rules()
    {
        List<string> lines = CSharpPromptSupport.BuildArchitectureAgentRuleLines().ToList();

        Assert.Contains(lines, line => line.Contains("backendFiles.path", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("solution project directory names", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("*Tests.cs exemplar", StringComparison.Ordinal));
    }
}
