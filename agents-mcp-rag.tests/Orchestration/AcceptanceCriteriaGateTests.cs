using agents_mcp_rag.Configuration;
using agents_mcp_rag.Infrastructure;
using agents_mcp_rag.Tests.Helpers;

namespace agents_mcp_rag.Tests.Orchestration;

public class AcceptanceCriteriaGateTests
{
    [Fact]
    public void EvaluateDeterministic_fails_when_production_build_not_passing()
    {
        WorkflowState state = WorkflowStateBuilder.Create("/repo", stack: new RepoStack(true, false));
        state.RequirementsSpec = new RequirementsSpec
        {
            AcceptanceCriteria =
            [
                new AcceptanceCriterion("AC-1", "Production build succeeds.")
            ]
        };
        state.BuildValidation = new AgentResult
        {
            AgentName = "BuildValidationAgent",
            Summary = "Build failed",
            ProductionBuildPassed = false
        };

        AcceptanceCriteriaReport report = AcceptanceCriteriaGate.EvaluateDeterministic(
            state,
            new AcceptanceCriteriaOptions { Enabled = true, RequireProductionBuildPass = true });

        Assert.Contains(report.Evaluations, evaluation => evaluation.Id == "GATE-BUILD" && !evaluation.Passed);
        Assert.Contains(report.Evaluations, evaluation => evaluation.Id == "AC-1" && !evaluation.Passed);
    }

    [Fact]
    public void EvaluateDeterministic_passes_when_deliverables_exist_on_disk()
    {
        using TempRepo repo = new();
        repo.WriteFile("src/Repositories/TimesheetRepository.cs", "class TimesheetRepository {}");
        WorkflowState state = WorkflowStateBuilder.Create(repo.Path, stack: new RepoStack(true, false));
        state.RequirementsSpec = new RequirementsSpec
        {
            AcceptanceCriteria = [new AcceptanceCriterion("AC-1", "Repository exposes CRUD methods.")]
        };
        state.ArchitecturePlan = new ArchitecturePlan
        {
            BackendFiles = [new ArchitectureDeliverable("src/Repositories/TimesheetRepository.cs")]
        };
        state.BuildValidation = new AgentResult
        {
            AgentName = "BuildValidationAgent",
            Summary = "Build passed",
            ProductionBuildPassed = true,
            TestsPassed = true
        };

        AcceptanceCriteriaReport report = AcceptanceCriteriaGate.EvaluateDeterministic(
            state,
            new AcceptanceCriteriaOptions());

        Assert.Contains(
            report.Evaluations,
            evaluation => evaluation.Id == "DELIVERABLE:src/Repositories/TimesheetRepository.cs" && evaluation.Passed);
    }

    [Fact]
    public void MergeReports_marks_missing_llm_evaluations_as_failed()
    {
        var requirements = new RequirementsSpec
        {
            AcceptanceCriteria =
            [
                new AcceptanceCriterion("AC-1", "User can create a timesheet."),
                new AcceptanceCriterion("AC-2", "User can list timesheets.")
            ]
        };
        var deterministic = new AcceptanceCriteriaReport
        {
            Evaluations =
            [
                new AcceptanceCriterionEvaluation("AC-1", "User can create a timesheet.", true, "Build passed.", "deterministic")
            ]
        };
        var llm = new AcceptanceCriteriaReport
        {
            Evaluations =
            [
                new AcceptanceCriterionEvaluation("AC-1", "User can create a timesheet.", true, "Controller and repository present.", "llm")
            ]
        };

        AcceptanceCriteriaReport merged = AcceptanceCriteriaGate.MergeReports(deterministic, llm, requirements);

        Assert.Equal(2, merged.Evaluations.Count);
        Assert.Contains(merged.Evaluations, evaluation => evaluation.Id == "AC-2" && !evaluation.Passed);
        Assert.False(merged.AllPassed);
    }
}
