using workflowX.Infrastructure.CodeApply.DotNet;
using workflowX.Tests.Helpers;

namespace workflowX.PlugIns.DotNet.Tests.CodeApply;

public class CSharpAssemblyMetadataGuardTests
{
    [Fact]
    public void TryValidateApply_rejects_assembly_info_path()
    {
        Assert.False(CSharpAssemblyMetadataGuard.TryValidateApply(
            "Acme.Tests/Properties/AssemblyInfo.cs",
            "[assembly: AssemblyCompany(\"Acme\")]",
            out string reason));
        Assert.Contains("AssemblyInfo.cs", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void TryValidateApply_rejects_assembly_company_in_source()
    {
        Assert.False(CSharpAssemblyMetadataGuard.TryValidateApply(
            "Acme.Tests/FooTests.cs",
            """
            using Xunit;
            [assembly: AssemblyCompany("Acme")]
            public class FooTests { }
            """,
            out string reason));
        Assert.Contains("Assembly*", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void TryValidateApply_allows_xunit_collection_behavior_in_test_source()
    {
        Assert.True(CSharpAssemblyMetadataGuard.TryValidateApply(
            "Acme.Tests/TestsSetup.cs",
            """
            using Xunit;
            [assembly: CollectionBehavior(DisableTestParallelization = true)]
            """,
            out _));
    }

    [Fact]
    public void ShouldRemoveStrayAssemblyInfoFile_detects_project_root_assembly_info()
    {
        using TempRepo repo = new();
        string path = repo.WriteFile(
            "Acme.Tests/Acme.Tests.AssemblyInfo.cs",
            "[assembly: System.Reflection.AssemblyCompanyAttribute(\"Acme\")]");

        Assert.True(CSharpAssemblyMetadataGuard.ShouldRemoveStrayAssemblyInfoFile(path));
    }
}
