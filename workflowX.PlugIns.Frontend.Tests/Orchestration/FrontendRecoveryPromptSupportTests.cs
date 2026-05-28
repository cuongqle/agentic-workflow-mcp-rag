namespace workflowX.PlugIns.Frontend.Tests.Orchestration;

public class FrontendRecoveryPromptSupportTests
{
    [Fact]
    public void BuildRuleLines_includes_import_and_module_guidance_without_csharp_terms()
    {
        var lines = FrontendRecoveryPromptSupport.BuildRuleLines().ToList();

        Assert.Contains(lines, line => line.Contains("import", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("TypeScript", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("constructor dependency", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains(".csproj", StringComparison.Ordinal));
    }
}
