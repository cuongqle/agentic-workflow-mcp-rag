using workflowX.Infrastructure;

namespace workflowX.Tests.Infrastructure;

public class RecoveryPathSupportTests
{
    [Theory]
    [InlineData(
        "SinglePageSample/SinglePageSample.Repository/SinglePageSample.Repository.csproj",
        "SinglePageSample.Repository/SinglePageSample.Repository.csproj")]
    [InlineData(
        "SinglePageSample/SinglePageSample.Application/modules/sample/tests/timesheetControllerSpec.js",
        "SinglePageSample.Application/modules/sample/tests/timesheetControllerSpec.js")]
    public void TryStripDuplicateRepositoryFolderPrefix_removes_leading_repo_segment(
        string proposed,
        string expected)
    {
        Assert.True(RecoveryPathSupport.TryStripDuplicateRepositoryFolderPrefix(proposed, out string canonical));
        Assert.Equal(expected, canonical, StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("SinglePageSample.Repository/TimesheetRepository.cs")]
    [InlineData("Acme.Tests/Acme.Tests.csproj")]
    [InlineData("src/modules/foo.spec.js")]
    public void TryStripDuplicateRepositoryFolderPrefix_leaves_canonical_paths_unchanged(string path)
    {
        Assert.False(RecoveryPathSupport.TryStripDuplicateRepositoryFolderPrefix(path, out string canonical));
        Assert.Equal(path, canonical, StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(
        "SinglePageSample/SinglePageSample.UnitTest/RepositoryTest/TimesheetRepositoryTests.cs",
        "SinglePageSample.UnitTest/RepositoryTest/TimesheetRepositoryTests.cs")]
    [InlineData(
        "SinglePageSample.SinglePageSample.UnitTest/RepositoryTest/TimesheetRepositoryTests.cs",
        "SinglePageSample.UnitTest/RepositoryTest/TimesheetRepositoryTests.cs")]
    public void CanonicalizeRecoveryPath_fixes_duplicated_repo_prefix(string proposed, string expected)
    {
        string canonical = RecoveryPathSupport.CanonicalizeRecoveryPath(proposed);
        Assert.Equal(expected, canonical, StringComparer.OrdinalIgnoreCase);
    }
}
