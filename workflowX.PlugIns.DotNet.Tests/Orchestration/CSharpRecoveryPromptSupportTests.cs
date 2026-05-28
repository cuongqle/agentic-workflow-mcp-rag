namespace workflowX.PlugIns.DotNet.Tests.Orchestration;

public class CSharpRecoveryPromptSupportTests
{
    [Fact]
    public void BuildRuleLines_includes_constructor_and_csproj_guidance()
    {
        var lines = CSharpRecoveryPromptSupport.BuildRuleLines().ToList();

        Assert.Contains(lines, line => line.Contains("constructor dependency", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains(".csproj", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("Parse/TryParse", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("[FromQuery]/[FromRoute]", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("protected existing infrastructure contracts", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("not declared on an interface", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("TypeScript", StringComparison.Ordinal));
    }
}
