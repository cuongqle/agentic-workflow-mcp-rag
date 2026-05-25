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
}
