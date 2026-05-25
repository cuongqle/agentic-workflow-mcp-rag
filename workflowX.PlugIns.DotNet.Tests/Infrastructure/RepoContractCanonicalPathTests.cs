using workflowX.Infrastructure;
using workflowX.Tests.Helpers;

namespace workflowX.PlugIns.DotNet.Tests.Infrastructure;

public class RepoContractCanonicalPathTests
{
    [Fact]
    public void ResolveCanonicalRelativePath_remaps_bare_repository_filename()
    {
        RepoContract contract = WorkflowStateBuilder.MinimalContract("/repo", new RepoStack(true, false));

        string canonical = contract.ResolveCanonicalRelativePath("TimesheetRepository.cs", string.Empty);

        Assert.Equal("src/Repositories/TimesheetRepository.cs", canonical);
    }

    [Fact]
    public void ResolveCanonicalRelativePath_keeps_interface_in_interfaces_folder()
    {
        RepoContract contract = WorkflowStateBuilder.MinimalContract("/repo", new RepoStack(true, false));

        string canonical = contract.ResolveCanonicalRelativePath("ITimesheetRepository.cs", string.Empty);

        Assert.Equal("src/Interfaces/ITimesheetRepository.cs", canonical);
    }

    [Fact]
    public void ResolveCanonicalRelativePath_preserves_already_canonical_path()
    {
        RepoContract contract = WorkflowStateBuilder.MinimalContract("/repo", new RepoStack(true, false));

        string input = "src/Repositories/EmployeeRepository.cs";
        string canonical = contract.ResolveCanonicalRelativePath(input, string.Empty);

        Assert.Equal(input, canonical);
    }

    [Fact]
    public void Discover_places_repository_implementations_outside_interfaces()
    {
        using TempRepo repo = new();
        repo.WriteFile("SinglePageSample.Repository/EmployeeRepository.cs", "class EmployeeRepository {}");
        repo.WriteFile("SinglePageSample.Repository/CompanyRepository.cs", "class CompanyRepository {}");
        repo.WriteFile("SinglePageSample.Repository/Interfaces/IEmployeeRepository.cs", "interface IEmployeeRepository {}");
        repo.WriteFile("SinglePageSample.Repository/Interfaces/ICompanyRepository.cs", "interface ICompanyRepository {}");
        repo.WriteFile("SinglePageSample.Repository/Interfaces/IRepository.cs", "interface IRepository {}");

        RepoContract contract = RepoContractDiscoverer.Discover(repo.Path);

        string bare = contract.ResolveCanonicalRelativePath("TimesheetRepository.cs", string.Empty);
        Assert.Equal("SinglePageSample.Repository/TimesheetRepository.cs", bare);

        string misplaced = DotNetRepoContractSupport.ResolveCanonicalRelativePath(
            contract,
            "SinglePageSample.Repository/Interfaces/TimesheetRepository.cs",
            string.Empty);
        Assert.Equal("SinglePageSample.Repository/TimesheetRepository.cs", misplaced);

        string iface = contract.ResolveCanonicalRelativePath(
            "SinglePageSample.Repository/Interfaces/ITimesheetRepository.cs",
            string.Empty);
        Assert.Equal("SinglePageSample.Repository/Interfaces/ITimesheetRepository.cs", iface);
    }
}
