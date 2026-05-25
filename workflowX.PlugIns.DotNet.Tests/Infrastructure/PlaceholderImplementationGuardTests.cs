using workflowX.Infrastructure.CodeApply.DotNet;

namespace workflowX.PlugIns.DotNet.Tests.Infrastructure;

public class PlaceholderImplementationGuardTests
{
    [Fact]
    public void Timesheet_entity_proposed_content_is_not_flagged_as_placeholder()
    {
        const string content = """
            using SinglePageSample.Db.DbStore;
            using System;

            namespace SinglePageSample.Repository.Entities
            {
                public class Timesheet : IEntity
                {
                    public int Id { get; set; }
                    public int EmployeeId { get; set; }
                    public DateTime Date { get; set; }
                    public double HoursWorked { get; set; }
                }
            }
            """;

        Assert.False(PlaceholderImplementationGuard.ContainsPlaceholderMarkers(content));
        Assert.True(PlaceholderImplementationGuard.TryValidate(content, out string reason));
        Assert.Empty(reason);
    }
}
