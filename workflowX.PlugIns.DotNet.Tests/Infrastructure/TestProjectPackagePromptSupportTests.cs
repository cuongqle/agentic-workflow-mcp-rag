using System.Text;
using workflowX.Infrastructure.Compliance.DotNet;

namespace workflowX.PlugIns.DotNet.Tests.Infrastructure;

public class TestProjectPackagePromptSupportTests
{
    [Fact]
    public void AppendRagExemplars_includes_moq_from_sibling_test_project()
    {
        string repo = CreateRepo(root =>
        {
            root.WriteFile(
                "Sample.UnitTest/Sample.UnitTest.csproj",
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <PackageReference Include="Moq" Version="4.20.72" />
                    <PackageReference Include="xunit" Version="2.9.2" />
                  </ItemGroup>
                </Project>
                """);
        });

        string text = BuildRagText(repo);

        Assert.Contains("Test project references", text, StringComparison.Ordinal);
        Assert.Contains("Moq", text, StringComparison.Ordinal);
        Assert.Contains("4.20.72", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AppendRagExemplars_includes_project_reference_from_sibling_test_project()
    {
        string repo = CreateRepo(root =>
        {
            root.WriteFile(
                "SinglePageSample.Repository/SinglePageSample.Repository.csproj",
                "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
            root.WriteFile(
                "SinglePageSample.UnitTest/SinglePageSample.UnitTest.csproj",
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <ProjectReference Include="..\SinglePageSample.Repository\SinglePageSample.Repository.csproj" />
                    <PackageReference Include="xunit" Version="2.9.2" />
                  </ItemGroup>
                </Project>
                """);
            root.WriteFile(
                "SinglePageSample.sln",
                """
                Microsoft Visual Studio Solution File, Format Version 12.00
                Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Repository", "SinglePageSample.Repository\SinglePageSample.Repository.csproj", "{A}"
                Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "UnitTest", "SinglePageSample.UnitTest\SinglePageSample.UnitTest.csproj", "{B}"
                """);
        });

        string text = BuildRagText(repo);

        Assert.Contains("ProjectReference Include=\"..\\SinglePageSample.Repository\\SinglePageSample.Repository.csproj\"", text, StringComparison.Ordinal);
        Assert.Contains("SinglePageSample.Repository/SinglePageSample.Repository.csproj", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AppendRagExemplars_reads_package_versions_from_directory_packages_props()
    {
        string repo = CreateRepo(root =>
        {
            root.WriteFile(
                "Directory.Packages.props",
                """
                <Project>
                  <ItemGroup>
                    <PackageVersion Include="FluentAssertions" Version="6.12.0" />
                  </ItemGroup>
                </Project>
                """);
            root.WriteFile(
                "Sample.UnitTest/Sample.UnitTest.csproj",
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <PackageReference Include="FluentAssertions" />
                    <PackageReference Include="xunit" Version="2.9.2" />
                  </ItemGroup>
                </Project>
                """);
        });

        string text = BuildRagText(repo);

        Assert.Contains("FluentAssertions", text, StringComparison.Ordinal);
        Assert.Contains("6.12.0", text, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildRuleLines_mentions_package_and_project_references()
    {
        IEnumerable<string> lines = TestProjectPackagePromptSupport.BuildRuleLines().ToList();
        Assert.Contains(lines, line => line.Contains("PackageReference", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("ProjectReference", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("Test project references", StringComparison.Ordinal));
    }

    private static string BuildRagText(string repo)
    {
        var sb = new StringBuilder();
        TestProjectPackagePromptSupport.AppendRagExemplars(sb, repo);
        return sb.ToString();
    }

    private static string CreateRepo(Action<TestRepoFixture> configure)
    {
        var fixture = new TestRepoFixture();
        configure(fixture);
        return fixture.RepoPath;
    }

    private sealed class TestRepoFixture
    {
        public string RepoPath { get; } = Path.Combine(Path.GetTempPath(), "workflowx-ref-" + Guid.NewGuid().ToString("N"));

        public TestRepoFixture()
        {
            Directory.CreateDirectory(RepoPath);
        }

        public void WriteFile(string relativePath, string content)
        {
            string absolute = Path.Combine(RepoPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            string? directory = Path.GetDirectoryName(absolute);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(absolute, content);
        }
    }
}
