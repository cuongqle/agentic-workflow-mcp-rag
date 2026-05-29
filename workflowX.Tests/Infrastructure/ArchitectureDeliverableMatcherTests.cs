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
    public void IsStrictArchitectureDeliverable_requires_exact_path()
    {
        const string planned = "SinglePageSample.WebAPI/Controllers/TimesheetController.cs";

        Assert.True(ArchitectureDeliverableMatcher.IsStrictArchitectureDeliverable(
            planned,
            [planned]));
        Assert.False(ArchitectureDeliverableMatcher.IsStrictArchitectureDeliverable(
            "SinglePageSample/SinglePageSample.WebAPI/Controllers/TimesheetController.cs",
            [planned]));
        Assert.False(ArchitectureDeliverableMatcher.IsStrictArchitectureDeliverable(
            "SinglePageSample.Repository/Controllers/TimesheetController.cs",
            [planned]));
    }

    [Fact]
    public void IsStrictArchitectureDeliverable_allows_planned_interface_companion_only()
    {
        var allowed = new[] { "SinglePageSample.Repository/TimesheetRepository.cs" };

        Assert.True(ArchitectureDeliverableMatcher.IsStrictArchitectureDeliverable(
            "SinglePageSample.Repository/Interfaces/ITimesheetRepository.cs",
            allowed));
        Assert.False(ArchitectureDeliverableMatcher.IsStrictArchitectureDeliverable(
            "SinglePageSample/SinglePageSample.Repository/Entities/Timesheet.cs",
            allowed));
    }

    [Fact]
    public void IsStrictArchitectureDeliverable_rejects_unlisted_test_path_with_duplicate_prefix()
    {
        var allowed = new[]
        {
            "SinglePageSample.Repository/TimesheetRepository.cs",
            "SinglePageSample.UnitTest/RepositoryTest/TimesheetRepositoryTests.cs"
        };

        Assert.False(ArchitectureDeliverableMatcher.IsStrictArchitectureDeliverable(
            "SinglePageSample/SinglePageSample.UnitTest/RepositoryTest/TimesheetRepositoryTests.cs",
            allowed));
        Assert.True(ArchitectureDeliverableMatcher.IsStrictArchitectureDeliverable(
            "SinglePageSample.UnitTest/RepositoryTest/TimesheetRepositoryTests.cs",
            allowed));
    }

    [Fact]
    public void PathsMatch_does_not_equate_different_project_segments_with_same_filename()
    {
        const string planned = "SinglePageSample/SinglePageSample.Api/Controllers/TimesheetController.cs";
        const string proposed = "SinglePageSample/SinglePageSample.WebAPI/Controllers/TimesheetController.cs";

        Assert.False(ArchitectureDeliverableMatcher.PathsMatch(proposed, planned));
    }

    [Fact]
    public void IsAllowedDeliverable_still_allows_loose_recovery_style_match()
    {
        var allowed = new[] { "SinglePageSample.Repository/TimesheetRepository.cs" };

        Assert.True(ArchitectureDeliverableMatcher.IsAllowedDeliverable(
            "SinglePageSample/SinglePageSample.Repository/Interfaces/ITimesheetRepository.cs",
            allowed));
    }
}
