namespace workflowX.Tests.Orchestration;

public class RequirementsSpecParserTests
{
    [Fact]
    public void TryParseJson_parses_acceptance_criteria()
    {
        const string json = """
            {
              "userStory": "As a user, I want timesheets, so that I can track hours.",
              "acceptanceCriteria": [
                { "id": "AC-1", "description": "Production build succeeds." },
                { "id": "AC-2", "description": "Unit tests pass for timesheet repository." }
              ],
              "inScope": ["CRUD API"],
              "outOfScope": ["Payroll export"],
              "risks": ["Legacy AngularJS patterns"]
            }
            """;

        bool parsed = RequirementsSpecParser.TryParseJson(json, out RequirementsSpec? spec, out string summary);

        Assert.True(parsed);
        Assert.NotNull(spec);
        Assert.Equal(2, spec!.AcceptanceCriteria.Count);
        Assert.Equal("AC-1", spec.AcceptanceCriteria[0].Id);
        Assert.Contains("Acceptance criteria:", summary);
    }
}
