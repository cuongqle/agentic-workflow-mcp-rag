using workflowX.Orchestration.Compliance;
using workflowX.Tests.Helpers;

namespace workflowX.Tests.Orchestration;

public class ArchitectureCoverageComplianceRuleTests
{
    [Fact]
    public void Evaluate_uses_disk_content_for_applied_files_instead_of_stale_backend_proposal()
    {
        using TempRepo repo = new();
        const string relativePath = "SinglePageSample.Repository/Entities/Timesheet.cs";
        const string cleanContent = """
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

        string absolutePath = Path.Combine(repo.Path, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        File.WriteAllText(absolutePath, cleanContent);

        WorkflowState state = WorkflowStateBuilder.Create(repo.Path, stack: new RepoStack(true, false));
        state.ArchitecturePlan = new ArchitecturePlan
        {
            BackendFiles = [new ArchitectureDeliverable(relativePath, "entity")]
        };
        state.Backend = new AgentResult
        {
            AgentName = "BackendDeveloperAgent",
            Summary = "stub proposal",
            ProposedFiles =
            [
                new GeneratedFile
                {
                    RelativePath = relativePath,
                    Content = "// TODO: implement entity\npublic class Timesheet {}"
                }
            ]
        };
        state.AppliedFiles.Add(relativePath);

        ComplianceContext context = ComplianceContextFactory.Create(state);
        var rule = new ArchitectureCoverageComplianceRule();

        List<AgentFinding> findings = rule.Evaluate(context).ToList();

        Assert.DoesNotContain(
            findings,
            finding => finding.Message.Contains("placeholder/stub content", StringComparison.OrdinalIgnoreCase));
    }
}
