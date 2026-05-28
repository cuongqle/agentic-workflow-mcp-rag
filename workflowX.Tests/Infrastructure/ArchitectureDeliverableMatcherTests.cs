using workflowX.Infrastructure;

namespace workflowX.Tests.Infrastructure;

public class ArchitectureDeliverableMatcherTests
{
    [Fact]
    public void PathsMatch_aligns_repo_prefix_and_nested_project_folders()
    {
        const string planned = "SinglePageSample.Repository/TimesheetRepository.cs";
        const string proposed = "SinglePageSample/SinglePageSample.Repository/TimesheetRepository.cs";

        Assert.True(ArchitectureDeliverableMatcher.PathsMatch(proposed, planned));
    }

    [Fact]
    public void IsAllowedDeliverable_accepts_synthesized_test_path_and_index_companion()
    {
        var allowed = new[]
        {
            "SinglePageSample.Repository/TimesheetRepository.cs",
            "SinglePageSample.UnitTest/RepositoryTest/TimesheetRepositoryTests.cs"
        };

        Assert.True(ArchitectureDeliverableMatcher.IsAllowedDeliverable(
            "SinglePageSample/SinglePageSample.UnitTest/RepositoryTest/TimesheetRepositoryTests.cs",
            allowed));
        Assert.True(ArchitectureDeliverableMatcher.IsAllowedDeliverable(
            "SinglePageSample/SinglePageSample.Repository/Indexes/TimesheetIndex.cs",
            allowed));
        Assert.True(ArchitectureDeliverableMatcher.IsAllowedDeliverable(
            "SinglePageSample/SinglePageSample.Repository/Interfaces/ITimesheetRepository.cs",
            allowed));
    }

    [Fact]
    public void IsAllowedDeliverable_uses_repo_contract_path_rules_and_entity_directory()
    {
        var contract = new RepoContract
        {
            RepoPath = "/tmp",
            Entity = new EntityConvention(
                "SinglePageSample.Repository/Entities",
                "IEntity",
                "SinglePageSample.Repository/Entities/Company.cs",
                null),
            LayerConventions = new LayerConventionProfiles(
            [
                new LayerConventionProfile(
                    RoleName: "Repository",
                    FileSuffix: "Repository.cs",
                    SampleCount: 2,
                    CanonicalDirectory: "SinglePageSample.Repository",
                    RequireInheritanceClause: false,
                    RequireMatchingRoleInterface: false,
                    RequireBaseConstructorCall: false,
                    RequiredInheritedTypeTokens: Array.Empty<string>(),
                    RequiredConstructorParamTypes: Array.Empty<string>(),
                    InterfacePairing: LayerInterfacePairingConvention.None)
            ]),
            PathRules =
            [
                new PathPlacementRule("Index.cs", "SinglePageSample.Repository/Indexes", null)
            ]
        };

        var allowed = new[] { "SinglePageSample.Repository/TimesheetRepository.cs" };

        Assert.True(ArchitectureDeliverableMatcher.IsAllowedDeliverable(
            "SinglePageSample/SinglePageSample.Repository/Entities/Timesheet.cs",
            allowed,
            contract));
        Assert.True(ArchitectureDeliverableMatcher.IsAllowedDeliverable(
            "SinglePageSample/SinglePageSample.Repository/Indexes/TimesheetIndex.cs",
            allowed,
            contract));
    }

    [Fact]
    public void IsAllowedDeliverable_rejects_unrelated_backend_file()
    {
        var allowed = new[] { "SinglePageSample.Repository/TimesheetRepository.cs" };

        Assert.False(ArchitectureDeliverableMatcher.IsAllowedDeliverable(
            "SinglePageSample/SinglePageSample.WebAPI/Program.cs",
            allowed));
    }
}
