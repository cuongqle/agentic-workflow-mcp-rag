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

}
