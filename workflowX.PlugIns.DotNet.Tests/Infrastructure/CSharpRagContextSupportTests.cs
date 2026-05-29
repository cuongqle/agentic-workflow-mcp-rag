using System.Text;
using workflowX.Infrastructure.Rag.DotNet;

namespace workflowX.PlugIns.DotNet.Tests.Infrastructure;

public class CSharpRagContextSupportTests
{
    [Fact]
    public void AppendProductionSourceExemplars_lists_non_test_cs_paths()
    {
        string repo = CreateRepo(root =>
        {
            root.WriteFile(
                "Acme.Repository/Entities/Employee.cs",
                "namespace Acme.Repository.Entities; public class Employee { }");
            root.WriteFile(
                "Acme.Repository/Acme.Repository.csproj",
                "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
            root.WriteFile(
                "Acme.Tests/Acme.Tests.csproj",
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
                  </ItemGroup>
                </Project>
                """);
            root.WriteFile("Acme.Tests/EmployeeRepositoryTests.cs", "class EmployeeRepositoryTests { }");
        });

        var sb = new StringBuilder();
        CSharpRagContextSupport.AppendProductionSourceExemplars(sb, repo);
        string text = sb.ToString();

        Assert.Contains("Production source exemplars", text, StringComparison.Ordinal);
        Assert.Contains("Acme.Repository/Entities/Employee.cs", text, StringComparison.Ordinal);
        Assert.DoesNotContain("EmployeeRepositoryTests.cs", text, StringComparison.Ordinal);
    }

    private static string CreateRepo(Action<TestRepoFixture> configure)
    {
        var fixture = new TestRepoFixture();
        configure(fixture);
        return fixture.RepoPath;
    }

    private sealed class TestRepoFixture
    {
        public string RepoPath { get; } = Path.Combine(Path.GetTempPath(), "workflowx-rag-" + Guid.NewGuid().ToString("N"));

        public TestRepoFixture() => Directory.CreateDirectory(RepoPath);

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
