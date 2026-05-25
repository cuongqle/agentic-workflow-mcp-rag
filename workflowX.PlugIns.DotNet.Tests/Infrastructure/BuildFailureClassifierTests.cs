using workflowX.Infrastructure.Compliance.DotNet;
using workflowX.Tests.Helpers;

namespace workflowX.PlugIns.DotNet.Tests.Infrastructure;

public class BuildFailureClassifierTests
{
    [Theory]
    [InlineData("Build FAILED.")]
    [InlineData("Build failed; inspect build output for details.")]
    [InlineData("")]
    public void IsSummaryBanner_detects_dotnet_summary_lines(string message)
    {
        Assert.True(BuildFailureClassifier.IsSummaryBanner(message));
    }

    [Fact]
    public void IsActionableFinding_ignores_banner_and_medium_severity()
    {
        var banner = new AgentFinding
        {
            Severity = FindingSeverity.High,
            Message = "Build FAILED."
        };
        var diagnostic = new AgentFinding
        {
            Severity = FindingSeverity.High,
            Message = "src/Foo.cs(12,5): error CS1002: ; expected"
        };
        var skipped = new AgentFinding
        {
            Severity = FindingSeverity.Medium,
            Message = "Build validation skipped."
        };

        Assert.False(BuildFailureClassifier.IsActionableFinding(banner));
        Assert.True(BuildFailureClassifier.IsActionableFinding(diagnostic));
        Assert.False(BuildFailureClassifier.IsActionableFinding(skipped));
    }

    [Fact]
    public void Analyze_classifies_test_project_paths_as_test_only()
    {
        var findings = new List<AgentFinding>
        {
            new()
            {
                Severity = FindingSeverity.High,
                Message = "MyApp.UnitTest/Repositories/FooTests.cs(4,1): error CS0246: type not found"
            },
            new()
            {
                Severity = FindingSeverity.High,
                Message = "Build FAILED."
            }
        };

        BuildFailureAnalysis analysis = BuildFailureClassifier.Analyze(findings);

        Assert.True(analysis.IsTestOnly);
        Assert.False(analysis.HasProductionFailures);
    }

    [Theory]
    [InlineData("tests/FooTests.cs", true)]
    [InlineData("src/Repositories/FooRepository.cs", false)]
    [InlineData("MyApp.UnitTest/Services/BarTests.cs", true)]
    public void IsTestArtifactPath_detects_test_paths(string path, bool expected)
    {
        Assert.Equal(expected, BuildFailureClassifier.IsTestArtifactPath(path));
    }
}
