using workflowX.Infrastructure;
using workflowX.Tests.Helpers;

namespace workflowX.PlugIns.Frontend.Tests.Infrastructure;

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
    public void Validate_skips_without_findings_when_initial_repo_has_no_package_json()
    {
        using var repo = CreateLegacyFrontendRepo(includePackageJson: false);

        RepoContract contract = RepoContractDiscoverer.Discover(repo.Path);

        AgentResult result = FrontendBuildValidationSupport.Validate(repo.Path, contract);

        Assert.Null(result.ProductionBuildPassed);
        Assert.Empty(result.Findings);
        Assert.Contains("skipped", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Null(contract.Frontend?.NpmProjectRoot);
    }

    [Fact]
    public void Validate_skips_without_findings_when_package_json_has_no_build_script()
    {
        using var repo = CreateLegacyFrontendRepo(includePackageJson: true, includeBuildScript: false);

        RepoContract contract = RepoContractDiscoverer.Discover(repo.Path);

        AgentResult result = FrontendBuildValidationSupport.Validate(repo.Path, contract);

        Assert.Null(result.ProductionBuildPassed);
        Assert.Empty(result.Findings);
        Assert.NotNull(contract.Frontend?.NpmProjectRoot);
        Assert.Contains("SinglePageSample.Web", contract.Frontend!.NpmProjectRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static TempRepo CreateLegacyFrontendRepo(bool includePackageJson, bool includeBuildScript = true)
    {
        var repo = new TempRepo();
        repo.WriteFile("SinglePageSample.Web/modules/employee/controllers/list.js", "export default {};");
        repo.WriteFile("SinglePageSample.Web/modules/employee/views/list.html", "<div></div>");
        if (includePackageJson)
        {
            string buildScript = includeBuildScript ? ",\n    \"build\": \"webpack\"" : string.Empty;
            repo.WriteFile(
                "SinglePageSample.Web/package.json",
                $$"""
                {
                  "name": "legacy-web",
                  "scripts": {
                    "start": "webpack-dev-server"{{buildScript}}
                  }
                }
                """);
        }

        return repo;
    }

    [Fact]
    public void ExtractBuildErrors_returns_empty_for_success_output()
    {
        List<AgentFinding> findings = FrontendBuildValidationSupport.ExtractBuildErrors("compiled successfully", string.Empty);
        Assert.Empty(findings);
    }
}
