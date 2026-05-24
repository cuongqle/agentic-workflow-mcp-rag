using agents_mcp_rag.Infrastructure;
using agents_mcp_rag.Tests.Helpers;

namespace agents_mcp_rag.PlugIns.Frontend.Tests.Infrastructure;

public class FrontendBuildValidationTests
{
    [Fact]
    public void ExtractBuildErrors_parses_typical_webpack_and_npm_messages()
    {
        const string output = """
            ERROR in ./src/app/module.ts
            Module not found: Error: Can't resolve './missing'
            Failed to compile.
            """;

        List<AgentFinding> findings = FrontendBuildValidationSupport.ExtractBuildErrors(output, string.Empty);

        Assert.NotEmpty(findings);
        Assert.All(findings, f => Assert.Equal(FindingSeverity.High, f.Severity));
        Assert.Contains(findings, f => f.Message.Contains("Module not found", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExtractBuildErrors_returns_empty_for_success_output()
    {
        List<AgentFinding> findings = FrontendBuildValidationSupport.ExtractBuildErrors("compiled successfully", string.Empty);
        Assert.Empty(findings);
    }
}
