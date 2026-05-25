using workflowX.Infrastructure;
using workflowX.Tests.Helpers;

namespace workflowX.Tests.Orchestration;

public class ArchitecturePlanParserTests
{
    [Fact]
    public void TryParseJson_parses_structured_plan()
    {
        const string json = """
            {
              "summary": "Add timesheet feature",
              "backendFiles": [
                { "path": "src/Repositories/TimesheetRepository.cs", "description": "data access" }
              ],
              "frontendFiles": [
                { "path": "web/modules/timesheets.js", "description": "controller" }
              ],
              "testStrategy": "run unit tests",
              "rollbackNotes": "revert commit"
            }
            """;

        bool parsed = ArchitecturePlanParser.TryParseJson(json, out ArchitecturePlan? plan, out string summary);

        Assert.True(parsed);
        Assert.NotNull(plan);
        Assert.Equal("Add timesheet feature", plan!.Rationale);
        Assert.Single(plan.BackendFiles);
        Assert.Equal("src/Repositories/TimesheetRepository.cs", plan.BackendFiles[0].Path);
        Assert.Single(plan.FrontendFiles);
        Assert.Equal("run unit tests", plan.TestStrategy);
        Assert.Contains("BACKEND_FILES:", summary);
        Assert.Contains("TimesheetRepository.cs", summary);
    }

    [Fact]
    public void ParseMarkdown_extracts_bold_sections_and_numbered_lists()
    {
        const string markdown = """
            **BACKEND_FILES:**

            1. SinglePageSample.Repository/Entities/Timesheet.cs: entity
            2. SinglePageSample.WebAPI/Controllers/TimesheetController.cs: controller

            **FRONTEND_FILES:**

            1. SinglePageSample/Application/modules/sample/controllers/timesheets.js: controller
            """;

        ArchitecturePlan? plan = ArchitecturePlanParser.ParseMarkdown(markdown);

        Assert.NotNull(plan);
        Assert.Equal(2, plan!.BackendFiles.Count);
        Assert.Single(plan.FrontendFiles);
        Assert.Equal("entity", plan.BackendFiles[0].Description);
    }

    [Fact]
    public void Resolve_prefers_architecture_plan_on_agent_result()
    {
        var structured = new ArchitecturePlan
        {
            BackendFiles = [new ArchitectureDeliverable("a.cs", "backend")]
        };
        var result = new AgentResult
        {
            Summary = "ignored markdown",
            ArchitecturePlan = structured
        };

        ArchitecturePlan resolved = ArchitecturePlanParser.Resolve(result);

        Assert.Same(structured, resolved);
    }
}
