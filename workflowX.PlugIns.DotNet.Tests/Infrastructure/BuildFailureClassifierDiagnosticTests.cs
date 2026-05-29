using workflowX.Infrastructure.Compliance.DotNet;

namespace workflowX.PlugIns.DotNet.Tests.Infrastructure;

public class BuildFailureClassifierDiagnosticTests
{
    [Fact]
    public void ReportsUnresolvedTypeOrNamespace_matches_compiler_wording()
    {
        const string message =
            "Acme.Tests/FooTests.cs(1,7): error CS0246: The type or namespace name 'Example' could not be found "
            + "(are you missing a using directive or an assembly reference?)";

        Assert.True(BuildFailureClassifier.ReportsUnresolvedTypeOrNamespace(message));
    }

    [Fact]
    public void ReportsDuplicateAssemblyAttribute_matches_compiler_wording()
    {
        const string message =
            "Acme.Tests/Acme.Tests.AssemblyInfo.cs(13, 12): error CS0579: Duplicate 'System.Reflection.AssemblyCompanyAttribute' attribute";

        Assert.True(BuildFailureClassifier.ReportsDuplicateAssemblyAttribute(message));
    }
}
