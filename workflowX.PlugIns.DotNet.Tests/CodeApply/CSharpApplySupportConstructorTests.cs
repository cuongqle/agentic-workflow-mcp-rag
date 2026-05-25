using workflowX.Infrastructure;
using workflowX.Tests.Helpers;

namespace workflowX.PlugIns.DotNet.Tests.CodeApply;

public class CSharpApplySupportConstructorTests
{
    [Fact]
    public async Task ApplyAsync_injects_shared_constructor_dependency_from_controller_exemplar()
    {
        using var repo = new TempRepo();
        repo.WriteFile(
            "SinglePageSample.WebAPI/Controllers/EmployeeController.cs",
            """
            namespace SinglePageSample.WebAPI.Controllers;

            public class EmployeeController
            {
                public EmployeeController(IEmployeeRepository employeeRepository, ICompanyRepository companyRepository)
                {
                }
            }
            """);
        repo.WriteFile(
            "SinglePageSample.WebAPI/Controllers/CompanyController.cs",
            """
            namespace SinglePageSample.WebAPI.Controllers;

            public class CompanyController
            {
                public CompanyController(ICompanyRepository companyRepository)
                {
                }
            }
            """);

        WorkflowState state = WorkflowStateBuilder.Create(repo.Path, stack: new RepoStack(true, false));
        state.Contract = RepoContractDiscoverer.Discover(repo.Path);
        state.Stage = WorkflowStage.Implementing;
        state.Backend = new AgentResult
        {
            ProposedFiles =
            [
                new GeneratedFile
                {
                    RelativePath = "SinglePageSample.WebAPI/Controllers/TimesheetController.cs",
                    Content = """
                        namespace SinglePageSample.WebAPI.Controllers;

                        public class TimesheetController
                        {
                            private readonly ITimesheetRepository TimesheetRepository;

                            public TimesheetController(ITimesheetRepository timesheetRepository)
                            {
                                TimesheetRepository = timesheetRepository;
                            }

                            public void GetAllTimesheets()
                            {
                                _ = this.CompanyRepository;
                            }
                        }
                        """
                }
            ]
        };

        ApplyResult result = await GeneratedFileApplier.ApplyAsync(state);

        Assert.Empty(result.RejectedFiles);
        Assert.Single(result.AppliedFiles);
        string written = File.ReadAllText(
            Path.Combine(repo.Path, "SinglePageSample.WebAPI/Controllers/TimesheetController.cs"));
        Assert.Contains("ICompanyRepository companyRepository", written, StringComparison.Ordinal);
        Assert.Contains("private readonly ICompanyRepository CompanyRepository", written, StringComparison.Ordinal);
        Assert.Contains("CompanyRepository = companyRepository", written, StringComparison.Ordinal);
    }
}
