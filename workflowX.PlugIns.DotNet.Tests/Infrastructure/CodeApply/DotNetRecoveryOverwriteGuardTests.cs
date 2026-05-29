using workflowX.Infrastructure.CodeApply.DotNet;
using workflowX.Tests.Helpers;

namespace workflowX.PlugIns.DotNet.Tests.Infrastructure.CodeApply;

public class DotNetRecoveryOverwriteGuardTests
{
    [Fact]
    public void TryValidateOverwrite_allows_csproj_listed_in_allowed_paths()
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Acme.Tests/TimesheetRepositoryTests.cs",
            "Acme.Tests/Acme.Tests.csproj"
        };

        Assert.True(DotNetRecoveryOverwriteGuard.TryValidateOverwrite(
            "/repo",
            "Acme.Tests/Acme.Tests.csproj",
            existedBefore: true,
            allowed,
            out _));
    }

    [Fact]
    public void TryValidateOverwrite_resolves_csproj_from_allowed_cs_error_path()
    {
        using TempRepo repo = new();
        repo.WriteFile(
            "Acme.Tests/Acme.Tests.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
              </ItemGroup>
            </Project>
            """);
        repo.WriteFile("Acme.Tests/TimesheetRepositoryTests.cs", "class TimesheetRepositoryTests { }");

        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Acme.Tests/TimesheetRepositoryTests.cs"
        };

        Assert.True(DotNetRecoveryOverwriteGuard.TryValidateOverwrite(
            repo.Path,
            "Acme.Tests/Acme.Tests.csproj",
            existedBefore: true,
            allowed,
            out _));
    }
}
