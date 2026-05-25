using workflowX.Tests.Helpers;

namespace workflowX.PlugIns.DotNet.Tests.Infrastructure;

public class LayerTestTemplateBuilderTests
{
    [Fact]
    public void ApplySubjectRename_renames_private_fields_bootstrap_setup_and_persist_test_names()
    {
        using var repo = new TempRepo();
        repo.WriteFile(
            "src/Repositories/TimesheetRepository.cs",
            """
            public class TimesheetRepository
            {
                public void Insert(Timesheet entity)
                {
                }
            }
            """);

        const string template = """
            public class EmployeeRepositoryTests
            {
                private IEmployeeRepository _employeeRepository;

                [TestInitialize]
                public void Setup()
                {
                    HotSpot.Reset();
                    _employeeRepository = HotSpot.Resolve<IEmployeeRepository>();
                }

                [TestMethod]
                public void Insert_ShouldPersistEmployee()
                {
                    _employeeRepository.Insert(new Employee());
                }
            }
            """;

        string renamed = LayerTestTemplateBuilder.ApplySubjectRename(
            template,
            "EmployeeRepository",
            "TimesheetRepository",
            repo.Path);

        Assert.Contains("HotSpot.Reset();", renamed, StringComparison.Ordinal);
        Assert.Contains("private ITimesheetRepository _timesheetRepository;", renamed, StringComparison.Ordinal);
        Assert.Contains("_timesheetRepository = HotSpot.Resolve<ITimesheetRepository>();", renamed, StringComparison.Ordinal);
        Assert.Contains("Insert_ShouldPersistTimesheet", renamed, StringComparison.Ordinal);
        Assert.DoesNotContain("_employeeRepository", renamed, StringComparison.Ordinal);
        Assert.DoesNotContain("IEmployeeRepository", renamed, StringComparison.Ordinal);
    }
}
