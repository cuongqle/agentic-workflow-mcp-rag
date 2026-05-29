using workflowX.Infrastructure.Compliance.DotNet;

namespace workflowX.PlugIns.DotNet.Tests.Infrastructure;

public class TestProjectPathSupportTests
{
    [Fact]
    public void TryResolveOwningCsproj_finds_test_project_next_to_tests_file()
    {
        using var repo = new TestRepoFixture();
        repo.WriteFile(
            "Acme.Tests/Acme.Tests.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
              </ItemGroup>
            </Project>
            """);
        repo.WriteFile("Acme.Tests/WidgetRepositoryTests.cs", "class WidgetRepositoryTests { }");

        string? csproj = TestProjectPathSupport.TryResolveOwningTestCsproj(
            repo.Path,
            "Acme.Tests/WidgetRepositoryTests.cs");

        Assert.Equal("Acme.Tests/Acme.Tests.csproj", csproj);
    }

    [Fact]
    public void ExpandWithOwningTestProjects_adds_csproj_for_test_compile_error_path()
    {
        using var repo = new TestRepoFixture();
        repo.WriteFile(
            "Acme.Tests/Acme.Tests.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
              </ItemGroup>
            </Project>
            """);
        repo.WriteFile("Acme.Tests/WidgetRepositoryTests.cs", "class WidgetRepositoryTests { }");

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Acme.Tests/WidgetRepositoryTests.cs"
        };

        TestProjectPathSupport.ExpandWithOwningTestProjects(repo.Path, paths);

        Assert.Contains("Acme.Tests/Acme.Tests.csproj", paths);
    }

    private sealed class TestRepoFixture : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "workflowx-tpp-" + Guid.NewGuid().ToString("N"));

        public TestRepoFixture() => Directory.CreateDirectory(Path);

        public void WriteFile(string relativePath, string content)
        {
            string absolute = System.IO.Path.Combine(Path, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            string? directory = System.IO.Path.GetDirectoryName(absolute);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(absolute, content);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
