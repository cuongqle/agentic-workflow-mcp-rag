using workflowX.Configuration;
using workflowX.Infrastructure;
using workflowX.Tests.Helpers;

namespace workflowX.Tests.Orchestration;

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

    [Fact]
    public void MergeReports_prefers_deterministic_test_pass_over_llm_fail()
    {
        var requirements = new RequirementsSpec
        {
            AcceptanceCriteria =
            [
                new AcceptanceCriterion(
                    "AC-4",
                    "When running tests from the UnitTest project, all related unit tests should pass successfully.")
            ]
        };
        var deterministic = new AcceptanceCriteriaReport
        {
            Evaluations =
            [
                new AcceptanceCriterionEvaluation(
                    "AC-4",
                    requirements.AcceptanceCriteria[0].Description,
                    true,
                    "Automated tests passed. Required test files on disk: SinglePageSample.UnitTest/RepositoryTest/TimesheetRepositoryTests.cs.",
                    "deterministic")
            ]
        };
        var llm = new AcceptanceCriteriaReport
        {
            Evaluations =
            [
                new AcceptanceCriterionEvaluation(
                    "AC-4",
                    requirements.AcceptanceCriteria[0].Description,
                    false,
                    "No evidence of complete unit tests in audit summary.",
                    "llm")
            ]
        };

        AcceptanceCriteriaReport merged = AcceptanceCriteriaGate.MergeReports(deterministic, llm, requirements);

        AcceptanceCriterionEvaluation ac4 = Assert.Single(merged.Evaluations, evaluation => evaluation.Id == "AC-4");
        Assert.True(ac4.Passed);
        Assert.Equal("deterministic", ac4.Source);
    }

    [Fact]
    public void EvaluateDeterministic_includes_required_test_files_on_disk_in_evidence()
    {
        using TempRepo repo = new();
        repo.WriteFile("SinglePageSample.Repository/EmployeeRepository.cs", "class EmployeeRepository {}");
        repo.WriteFile("SinglePageSample.Repository/TimesheetRepository.cs", "class TimesheetRepository {}");
        repo.WriteFile("SinglePageSample.UnitTest/RepositoryTest/EmployeeRepositoryTests.cs", "class EmployeeRepositoryTests {}");
        repo.WriteFile("SinglePageSample.UnitTest/RepositoryTest/TimesheetRepositoryTests.cs", "class TimesheetRepositoryTests {}");

        WorkflowState state = WorkflowStateBuilder.Create(repo.Path, stack: new RepoStack(true, false));
        state.RequirementsSpec = new RequirementsSpec
        {
            AcceptanceCriteria =
            [
                new AcceptanceCriterion("AC-4", "All related unit tests should pass successfully.")
            ]
        };
        state.ArchitecturePlan = new ArchitecturePlan
        {
            BackendFiles = [new ArchitectureDeliverable("SinglePageSample.Repository/TimesheetRepository.cs")]
        };
        state.AppliedFiles.Add("SinglePageSample.Repository/TimesheetRepository.cs");
        state.AppliedFiles.Add("SinglePageSample.UnitTest/RepositoryTest/TimesheetRepositoryTests.cs");
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

        AcceptanceCriterionEvaluation ac4 = Assert.Single(
            report.Evaluations,
            evaluation => evaluation.Id == "AC-4");
        Assert.True(ac4.Passed);
        Assert.Contains("Automated tests passed.", ac4.Evidence);
        Assert.Contains("TimesheetRepositoryTests.cs", ac4.Evidence);
    }
}
