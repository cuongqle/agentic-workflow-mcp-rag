using workflowX.Infrastructure.CodeApply.DotNet;

namespace workflowX.PlugIns.DotNet.Tests.CodeApply;

public class ParseConversionGuardTests
{
    [Fact]
    public void TryValidate_rejects_parse_on_already_typed_member()
    {
        const string content = """
            namespace Sample;

            public class TimesheetController
            {
                public void Save(Timesheet timesheet)
                {
                    _ = this.EmployeeRepository.GetById(int.Parse(timesheet.EmployeeId));
                }
            }

            public class Timesheet
            {
                public int EmployeeId { get; set; }
            }
            """;

        Assert.False(ParseConversionGuard.TryValidate(content, out string reason));
        Assert.Contains("already typed", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryValidate_allows_parse_on_string_member()
    {
        const string content = """
            namespace Sample;

            public class Controller
            {
                public int Map(Request request) => int.Parse(request.EmployeeId);
            }

            public class Request
            {
                public string EmployeeId { get; set; } = string.Empty;
            }
            """;

        Assert.True(ParseConversionGuard.TryValidate(content, out string reason));
        Assert.Empty(reason);
    }
}
